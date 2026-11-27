using DiscordBot.Core.AI;
using DiscordBot.DTO.TechWatch;
using DiscordBot.Features.TechWatch;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Features.TechWatch;

[TestFixture]
public class TUGeminiSummarizerServiceTests
{
    private sealed class StubGeminiClientService(string? responseText, string? apiKey = "test-api-key")
        : GeminiClientService(
            NullLogger<GeminiClientService>.Instance,
            new FakeTimeProvider(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(apiKey is null ? [] : [new KeyValuePair<string, string?>("GEMINI_API_KEY", apiKey)])
                .Build())
    {
        public string? LastPreferredModel { get; private set; }
        public int CallCount { get; private set; }

        public override Task<string?> GenerateContentAsync(
            string prompt,
            string? preferredModel = null,
            string? responseMimeType = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPreferredModel = preferredModel;
            return Task.FromResult(responseText);
        }
    }

    private static List<NewsArticleDto> CreateSampleArticles() =>
    [
        new NewsArticleDto { Title = "Article 1", Url = "https://example.com/1", Source = "Dev.to", PublishedAt = DateTimeOffset.UtcNow },
        new NewsArticleDto { Title = "Article 2", Url = "https://example.com/2", Source = "HackerNews", PublishedAt = DateTimeOffset.UtcNow }
    ];

    [Test]
    public async Task SummarizeAndFormatArticlesAsync_ShouldNormalizeBulletsFromGeminiResponse()
    {
        // Arrange
        var stubClient = new StubGeminiClientService("* **Titre traduit**\n  <https://example.com/1>\n- **Autre article**\n  <https://example.com/2>");
        var service = new GeminiSummarizerService(stubClient, NullLogger<GeminiSummarizerService>.Instance);
        var settings = new TechWatchSettingsDto { GeminiModel = "gemini-2.5-flash", MaxArticles = 5 };

        // Act
        var result = await service.SummarizeAndFormatArticlesAsync(CreateSampleArticles(), settings);

        // Assert
        result.Should().Be("• **Titre traduit**\n  <https://example.com/1>\n• **Autre article**\n  <https://example.com/2>");
        stubClient.LastPreferredModel.Should().Be("gemini-2.5-flash");
        stubClient.CallCount.Should().Be(1);
    }

    [Test]
    public async Task SummarizeAndFormatArticlesAsync_ShouldReturnDirectFallbackWhenGeminiReturnsNull()
    {
        // Arrange
        var stubClient = new StubGeminiClientService(null);
        var service = new GeminiSummarizerService(stubClient, NullLogger<GeminiSummarizerService>.Instance);
        var settings = new TechWatchSettingsDto { GeminiModel = "gemini-2.5-flash", MaxArticles = 1 };

        // Act
        var result = await service.SummarizeAndFormatArticlesAsync(CreateSampleArticles(), settings);

        // Assert
        result.Should().Contain("• **Article 1**");
        result.Should().Contain("<https://example.com/1>");
        result.Should().NotContain("Article 2");
    }

    [Test]
    public async Task SummarizeAndFormatArticlesAsync_ShouldReturnEmptyMessageWithoutCallingGeminiWhenNoArticles()
    {
        // Arrange
        var stubClient = new StubGeminiClientService("unused");
        var service = new GeminiSummarizerService(stubClient, NullLogger<GeminiSummarizerService>.Instance);

        // Act
        var result = await service.SummarizeAndFormatArticlesAsync([], new TechWatchSettingsDto());

        // Assert
        result.Should().Contain("Aucun nouvel article");
        stubClient.CallCount.Should().Be(0);
    }
}
