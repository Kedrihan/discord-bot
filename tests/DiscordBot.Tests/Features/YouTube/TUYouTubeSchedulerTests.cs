using Discord.WebSocket;
using DiscordBot.DTO;
using DiscordBot.Features.YouTube;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Features.YouTube;

[TestFixture]
public class TUYouTubeSchedulerTests
{
    private TestBotDbContextFactory _dbContextFactory = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContextFactory = new TestBotDbContextFactory();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContextFactory.Dispose();
    }

    [Test]
    public async Task YouTubeScheduler_ShouldExecuteInitialDelayAndStopCleanly()
    {
        // Arrange
        var configService = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);
        await configService.SaveSettingsAsync(new YouTubeSettingsDto
        {
            IsEnabled = true,
            CheckIntervalMinutes = 15
        });

        var historyService = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var feedClient = new YouTubeFeedClient(new HttpClient(new MockHttpMessageHandler("{}")), NullLogger<YouTubeFeedClient>.Instance);
        var notificationService = new YouTubeNotificationService(discordClient, configService, historyService, feedClient, _timeProvider, NullLogger<YouTubeNotificationService>.Instance);

        var scheduler = new YouTubeScheduler(configService, notificationService, _timeProvider, NullLogger<YouTubeScheduler>.Instance);

        using var cts = new CancellationTokenSource();

        // Act: Start scheduler
        var executeTask = scheduler.StartAsync(cts.Token);

        // Wait until ExecuteAsync starts and enters initial delay
        for (var i = 0; i < 50 && !scheduler.IsWaitingForInitialDelay; i++)
        {
            await Task.Delay(10);
        }

        // Assert initial delay state
        scheduler.IsWaitingForInitialDelay.Should().BeTrue();

        // Advance past initial delay (15s)
        _timeProvider.Advance(TimeSpan.FromSeconds(16));

        // Cancel token to stop scheduler loop
        await cts.CancelAsync();
        await scheduler.StopAsync(CancellationToken.None);

        await executeTask;
        scheduler.IsWaitingForInitialDelay.Should().BeFalse();
    }

    private class MockHttpMessageHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
