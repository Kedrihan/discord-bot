using DiscordBot.DTO;
using DiscordBot.Services;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Features.Twitch;

[TestFixture]
public class TUMultiGuildTwitchTests
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
    public async Task TwitchStateService_TwoGuildsTrackingSameStreamer_ShouldIsolateActiveStreams()
    {
        var stateService = new TwitchStateService(_dbContextFactory, NullLogger<TwitchStateService>.Instance);

        var stateGuild1 = new TwitchActiveStreamStateDto
        {
            GuildId = "guild_1",
            TwitchUsername = "zerator",
            StreamId = "stream_111",
            DiscordChannelId = "channel_101",
            DiscordMessageId = 1111UL,
            StartedAt = _timeProvider.GetUtcNow()
        };

        var stateGuild2 = new TwitchActiveStreamStateDto
        {
            GuildId = "guild_2",
            TwitchUsername = "zerator",
            StreamId = "stream_111",
            DiscordChannelId = "channel_202",
            DiscordMessageId = 2222UL,
            StartedAt = _timeProvider.GetUtcNow()
        };

        // Act: Enregistrement indépendant pour les deux serveurs
        await stateService.SetActiveStreamAsync(stateGuild1);
        await stateService.SetActiveStreamAsync(stateGuild2);

        // Assert: Vérification de l'isolation
        var retrievedG1 = stateService.GetActiveStream("guild_1", "zerator");
        var retrievedG2 = stateService.GetActiveStream("guild_2", "zerator");

        retrievedG1.Should().NotBeNull();
        retrievedG1!.DiscordMessageId.Should().Be(1111UL);
        retrievedG1.DiscordChannelId.Should().Be("channel_101");

        retrievedG2.Should().NotBeNull();
        retrievedG2!.DiscordMessageId.Should().Be(2222UL);
        retrievedG2.DiscordChannelId.Should().Be("channel_202");

        var allStreamsForUser = stateService.GetActiveStreams("zerator");
        allStreamsForUser.Should().HaveCount(2);

        // Act: Retirer le stream de Guild 1 uniquement
        await stateService.RemoveActiveStreamAsync("guild_1", "zerator");

        stateService.GetActiveStream("guild_1", "zerator").Should().BeNull();
        stateService.GetActiveStream("guild_2", "zerator").Should().NotBeNull("Guild 2 must remain active!");
    }

    [Test]
    public async Task TwitchClipHistoryService_TwoGuildsTrackingSameStreamer_ShouldIsolateColdStartAndClips()
    {
        var clipHistory = new TwitchClipHistoryService(_dbContextFactory, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        const string broadcasterId = "broadcaster_42";

        // Guild 1 initialise le cold-start
        await clipHistory.InitializeBroadcasterAsync("guild_1", broadcasterId, ["clip_old_1", "clip_old_2"]);

        // Assert: Guild 1 est initialisée, Guild 2 NE l'est PAS
        clipHistory.IsBroadcasterInitialized("guild_1", broadcasterId).Should().BeTrue();
        clipHistory.IsBroadcasterInitialized("guild_2", broadcasterId).Should().BeFalse();

        // Guild 1 a vu clip_old_1, mais pas Guild 2
        clipHistory.IsClipSeen("guild_1", "clip_old_1").Should().BeTrue();
        clipHistory.IsClipSeen("guild_2", "clip_old_1").Should().BeFalse();

        // Guild 1 marque un nouveau clip
        await clipHistory.MarkClipsAsSeenAsync("guild_1", ["clip_new_3"], broadcasterId);

        clipHistory.IsClipSeen("guild_1", "clip_new_3").Should().BeTrue();
        clipHistory.IsClipSeen("guild_2", "clip_new_3").Should().BeFalse("Guild 2 has not seen this clip yet.");
    }

    [Test]
    public async Task TwitchConfigService_ShouldPersistGuildIdForTrackedChannels()
    {
        var configService = new TwitchConfigService(_dbContextFactory, NullLogger<TwitchConfigService>.Instance);

        var settings = new TwitchSettingsDto
        {
            IsEnabled = true,
            Channels =
            [
                new TrackedTwitchChannelDto { GuildId = "guild_alpha", TwitchUsername = "kameto", DiscordChannelId = "111" },
                new TrackedTwitchChannelDto { GuildId = "guild_beta", TwitchUsername = "kameto", DiscordChannelId = "222" }
            ]
        };

        await configService.SaveSettingsAsync(settings);

        var reloaded = await configService.GetSettingsAsync();
        reloaded.Channels.Should().HaveCount(2);
        reloaded.Channels.Should().Contain(c => c.GuildId == "guild_alpha" && c.TwitchUsername == "kameto" && c.DiscordChannelId == "111");
        reloaded.Channels.Should().Contain(c => c.GuildId == "guild_beta" && c.TwitchUsername == "kameto" && c.DiscordChannelId == "222");
    }
}
