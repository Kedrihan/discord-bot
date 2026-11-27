using DiscordBot.Core.AI;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Core.AI;

[TestFixture]
public class TUGeminiClientServiceTests
{
    private sealed class TestableGeminiClientService(
        TimeProvider timeProvider,
        IConfiguration configuration,
        Func<string, int, string?, Task<string?>> handler)
        : GeminiClientService(NullLogger<GeminiClientService>.Instance, timeProvider, configuration)
    {
        private readonly Dictionary<string, int> _attemptsByModel = new(StringComparer.OrdinalIgnoreCase);

        public List<string> CallHistory { get; } = [];
        public string? LastResponseMimeType { get; private set; }

        protected override Task<string?> InvokeModelAsync(
            string model,
            string prompt,
            string apiKey,
            string? responseMimeType,
            CancellationToken cancellationToken)
        {
            CallHistory.Add(model);
            LastResponseMimeType = responseMimeType;
            var attempt = _attemptsByModel.TryGetValue(model, out var count) ? count + 1 : 1;
            _attemptsByModel[model] = attempt;
            return handler(model, attempt, responseMimeType);
        }
    }

    private static IConfiguration CreateConfig(string? apiKey = "test-api-key") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(apiKey is null ? [] : [new KeyValuePair<string, string?>("GEMINI_API_KEY", apiKey)])
            .Build();

    [Test]
    public void BuildModelFallbackChain_ShouldStartWithConfiguredModelAndDeduplicate()
    {
        var chain = GeminiClientService.BuildModelFallbackChain("gemini-flash-latest");

        chain.Should().Equal(
            "gemini-flash-latest",
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-flash-lite-latest");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("gemini-1.5-flash")]
    [TestCase("gemini-2.0-flash")]
    public void BuildModelFallbackChain_ShouldNormalizeLegacyOrEmptyModel(string? configuredModel)
    {
        var chain = GeminiClientService.BuildModelFallbackChain(configuredModel);

        chain.Should().Equal(
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-flash-latest",
            "gemini-flash-lite-latest");
    }

    [Test]
    public async Task GenerateContentAsync_ShouldRetryTransientErrorWithBackoffAndSucceed()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var service = new TestableGeminiClientService(
            fakeTime,
            CreateConfig(),
            (_, attempt, _) =>
            {
                if (attempt < 3)
                {
                    throw new HttpRequestException("503 Service Unavailable");
                }

                return Task.FromResult<string?>("{\"verdict\":\"OK\"}");
            });

        // Act
        var task = service.GenerateContentAsync("Prompt test", preferredModel: "gemini-2.5-flash", responseMimeType: "application/json");
        fakeTime.Advance(TimeSpan.FromSeconds(5));
        await Task.Yield();
        fakeTime.Advance(TimeSpan.FromSeconds(15));
        var result = await task;

        // Assert
        result.Should().Be("{\"verdict\":\"OK\"}");
        service.LastResponseMimeType.Should().Be("application/json");
        service.CallHistory.Should().Equal("gemini-2.5-flash", "gemini-2.5-flash", "gemini-2.5-flash");
    }

    [Test]
    public async Task GenerateContentAsync_ShouldFallbackToNextModelAfterRetriesExhausted()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var service = new TestableGeminiClientService(
            fakeTime,
            CreateConfig(),
            (model, _, _) =>
            {
                if (model == "gemini-flash-latest")
                {
                    throw new HttpRequestException("503 high demand");
                }

                return Task.FromResult<string?>("Réponse de secours");
            });

        // Act
        var task = service.GenerateContentAsync("Prompt test", preferredModel: "gemini-flash-latest");
        fakeTime.Advance(TimeSpan.FromSeconds(5));
        await Task.Yield();
        fakeTime.Advance(TimeSpan.FromSeconds(15));
        var result = await task;

        // Assert
        result.Should().Be("Réponse de secours");
        service.CallHistory.Should().Equal(
            "gemini-flash-latest",
            "gemini-flash-latest",
            "gemini-flash-latest",
            "gemini-2.5-flash");
    }

    [Test]
    public async Task GenerateContentAsync_ShouldSkipRetriesOnNonTransientErrorAndTryNextModel()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var service = new TestableGeminiClientService(
            fakeTime,
            CreateConfig(),
            (model, _, _) =>
            {
                if (model == "gemini-unknown")
                {
                    throw new InvalidOperationException("Model not found");
                }

                return Task.FromResult<string?>("OK");
            });

        // Act
        var result = await service.GenerateContentAsync("Prompt test", preferredModel: "gemini-unknown");

        // Assert
        result.Should().Be("OK");
        service.CallHistory.Should().Equal("gemini-unknown", "gemini-2.5-flash");
    }

    [Test]
    public async Task GenerateContentAsync_ShouldReturnNullWhenAllModelsFail()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider();
        var service = new TestableGeminiClientService(
            fakeTime,
            CreateConfig(),
            (_, _, _) => throw new InvalidOperationException("All down"));

        // Act
        var result = await service.GenerateContentAsync("Prompt test", preferredModel: "gemini-2.5-flash");

        // Assert
        result.Should().BeNull();
        service.CallHistory.Should().Equal(
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-flash-latest",
            "gemini-flash-lite-latest");
    }
}
