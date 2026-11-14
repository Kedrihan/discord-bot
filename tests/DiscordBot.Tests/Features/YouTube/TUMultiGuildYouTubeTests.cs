using DiscordBot.DTO;
using DiscordBot.Features.YouTube;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Features.YouTube;

[TestFixture]
public class TUMultiGuildYouTubeTests
{
    private TestBotDbContextFactory _dbContextFactory = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContextFactory = new TestBotDbContextFactory();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 14, 0, 0, TimeSpan.Zero));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContextFactory.Dispose();
    }

    [Test]
    public async Task YouTubeHistoryService_TwoGuildsTrackingSameChannel_ShouldIsolateColdStartAndHistory()
    {
        var historyService = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);
        const string channelId = "UC_TEST_CHANNEL_123";

        // Guild Alpha initialise le cold-start
        await historyService.InitializeChannelAsync("guild_alpha", channelId, ["video_old_1", "video_old_2"]);

        // Assert: Guild Alpha initialisée, Guild Beta NON initialisée
        historyService.IsChannelInitialized("guild_alpha", channelId).Should().BeTrue();
        historyService.IsChannelInitialized("guild_beta", channelId).Should().BeFalse();

        // Guild Alpha a vu video_old_1, mais pas Guild Beta
        historyService.IsVideoSeen("guild_alpha", "video_old_1").Should().BeTrue();
        historyService.IsVideoSeen("guild_beta", "video_old_1").Should().BeFalse();

        // Guild Alpha marque une nouvelle vidéo
        await historyService.MarkVideosAsSeenAsync("guild_alpha", ["video_new_3"], channelId);

        historyService.IsVideoSeen("guild_alpha", "video_new_3").Should().BeTrue();
        historyService.IsVideoSeen("guild_beta", "video_new_3").Should().BeFalse("Guild Beta has not seen this video yet.");
    }

    [Test]
    public async Task YouTubeConfigService_ShouldPersistGuildIdForTrackedChannels()
    {
        var configService = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);

        var settings = new YouTubeSettingsDto
        {
            IsEnabled = true,
            CheckIntervalMinutes = 15,
            Channels =
            [
                new TrackedYouTubeChannelDto { GuildId = "guild_10", ChannelId = "UC_CHAN_1", ChannelTitle = "Chan 1", DiscordChannelId = "1001" },
                new TrackedYouTubeChannelDto { GuildId = "guild_20", ChannelId = "UC_CHAN_1", ChannelTitle = "Chan 1", DiscordChannelId = "2002" }
            ]
        };

        await configService.SaveSettingsAsync(settings);

        var reloaded = await configService.GetSettingsAsync();
        reloaded.Channels.Should().HaveCount(2);
        reloaded.Channels.Should().Contain(c => c.GuildId == "guild_10" && c.ChannelId == "UC_CHAN_1" && c.DiscordChannelId == "1001");
        reloaded.Channels.Should().Contain(c => c.GuildId == "guild_20" && c.ChannelId == "UC_CHAN_1" && c.DiscordChannelId == "2002");
    }
}
