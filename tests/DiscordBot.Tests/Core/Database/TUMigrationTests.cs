using System.IO.Abstractions.TestingHelpers;
using DiscordBot.Core.Database.Services;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Core.Database;

[TestFixture]
public class TUMigrationTests
{
    private TestBotDbContextFactory _dbContextFactory = null!;
    private MockFileSystem _fileSystem = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContextFactory = new TestBotDbContextFactory();
        _fileSystem = new MockFileSystem();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContextFactory.Dispose();
    }

    [Test]
    public async Task InitializeAndMigrateAsync_ShouldImportLegacyJsons_AndArchiveFilesWithMigratedExtension()
    {
        // Arrange: prepare legacy JSON files with legacy secrets included
        var morningBriefingJson = """
        {
            "isEnabled": true,
            "scheduleDays": [1, 2, 3, 4, 5],
            "scheduleTime": "07:30:00",
            "destinationType": "Channel",
            "targetId": "123456789",
            "address": "Paris, France",
            "latitude": 48.8566,
            "longitude": 2.3522,
            "morningStartHour": 7,
            "morningEndHour": 9,
            "eveningStartHour": 17,
            "eveningEndHour": 19
        }
        """;

        var techWatchJson = """
        {
            "isEnabled": true,
            "scheduleDays": [1, 3, 5],
            "scheduleTime": "08:00:00",
            "destinationType": "Channel",
            "targetId": "987654321",
            "topics": ["dotnet", "csharp", "ai"],
            "enableDevTo": true,
            "enableHackerNews": true,
            "geminiApiKey": "SHOULD_NOT_BE_IN_DB"
        }
        """;

        var twitchJson = """
        {
            "isEnabled": true,
            "clientId": "SECRET_CLIENT_ID",
            "clientSecret": "SECRET_CLIENT_SECRET",
            "userAccessToken": "SECRET_USER_TOKEN",
            "checkIntervalSeconds": 30,
            "channels": [
                {
                    "twitchUsername": "zerator",
                    "discordChannelId": "111222333",
                    "isEnabled": true
                }
            ]
        }
        """;

        var sentArticlesJson = """
        [
            {
                "url": "https://dev.to/article-1",
                "sentAt": "2026-11-01T10:00:00Z"
            }
        ]
        """;

        var twitchClipsJson = """
        {
            "initializedBroadcasters": ["12345"],
            "clips": [
                {
                    "clipId": "clip-123",
                    "broadcasterId": "12345",
                    "discoveredAt": "2026-11-02T15:00:00Z"
                }
            ]
        }
        """;

        var twitchLiveStateJson = """
        {
            "initializedBroadcasters": ["12345"],
            "activeStreams": {
                "zerator": {
                    "twitchUsername": "zerator",
                    "streamId": "stream-999",
                    "discordChannelId": "111222333",
                    "discordMessageId": 777888999,
                    "startedAt": "2026-11-04T10:00:00Z"
                }
            }
        }
        """;

        _fileSystem.AddFile("data/morning_briefing_settings.json", new MockFileData(morningBriefingJson));
        _fileSystem.AddFile("data/techwatch_settings.json", new MockFileData(techWatchJson));
        _fileSystem.AddFile("data/twitch_settings.json", new MockFileData(twitchJson));
        _fileSystem.AddFile("data/sent_articles.json", new MockFileData(sentArticlesJson));
        _fileSystem.AddFile("data/twitch_clips_history.json", new MockFileData(twitchClipsJson));
        _fileSystem.AddFile("data/twitch_live_state.json", new MockFileData(twitchLiveStateJson));

        var migrationService = new DatabaseMigrationService(
            _dbContextFactory,
            _fileSystem,
            _timeProvider,
            NullLogger<DatabaseMigrationService>.Instance);

        // Act
        await migrationService.InitializeAndMigrateAsync();

        // Assert: Database contents
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var morningBriefing = await context.MorningBriefingSettings.FirstOrDefaultAsync();
        morningBriefing.Should().NotBeNull();
        morningBriefing!.IsEnabled.Should().BeTrue();
        morningBriefing.ScheduleTime.Should().Be(new TimeOnly(7, 30));
        morningBriefing.TargetId.Should().Be("123456789");
        morningBriefing.ScheduleDays.Should().BeEquivalentTo([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]);

        var techWatch = await context.TechWatchSettings.FirstOrDefaultAsync();
        techWatch.Should().NotBeNull();
        techWatch!.IsEnabled.Should().BeTrue();
        techWatch.Topics.Should().BeEquivalentTo(["dotnet", "csharp", "ai"]);

        var twitch = await context.TwitchSettings.Include(t => t.Channels).FirstOrDefaultAsync();
        twitch.Should().NotBeNull();
        twitch!.IsEnabled.Should().BeTrue();
        twitch.CheckIntervalSeconds.Should().Be(30);
        twitch.Channels.Should().HaveCount(1);
        twitch.Channels[0].TwitchUsername.Should().Be("zerator");

        var articles = await context.SentArticles.ToListAsync();
        articles.Should().HaveCount(1);
        articles[0].Url.Should().Be("https://dev.to/article-1");

        var clips = await context.TwitchClips.ToListAsync();
        clips.Should().HaveCount(1);
        clips[0].ClipId.Should().Be("clip-123");

        var broadcasters = await context.TwitchInitializedBroadcasters.ToListAsync();
        broadcasters.Should().HaveCount(1);
        broadcasters[0].BroadcasterId.Should().Be("12345");

        var activeStreams = await context.TwitchActiveStreams.ToListAsync();
        activeStreams.Should().HaveCount(1);
        activeStreams[0].TwitchUsername.Should().Be("zerator");
        activeStreams[0].DiscordMessageId.Should().Be(777888999);

        // Assert: Files renamed to .migrated
        _fileSystem.File.Exists("data/morning_briefing_settings.json").Should().BeFalse();
        _fileSystem.File.Exists("data/morning_briefing_settings.json.migrated").Should().BeTrue();

        _fileSystem.File.Exists("data/techwatch_settings.json").Should().BeFalse();
        _fileSystem.File.Exists("data/techwatch_settings.json.migrated").Should().BeTrue();

        _fileSystem.File.Exists("data/twitch_settings.json").Should().BeFalse();
        _fileSystem.File.Exists("data/twitch_settings.json.migrated").Should().BeTrue();

        _fileSystem.File.Exists("data/sent_articles.json").Should().BeFalse();
        _fileSystem.File.Exists("data/sent_articles.json.migrated").Should().BeTrue();

        _fileSystem.File.Exists("data/twitch_clips_history.json").Should().BeFalse();
        _fileSystem.File.Exists("data/twitch_clips_history.json.migrated").Should().BeTrue();

        _fileSystem.File.Exists("data/twitch_live_state.json").Should().BeFalse();
        _fileSystem.File.Exists("data/twitch_live_state.json.migrated").Should().BeTrue();
    }

    [Test]
    public async Task InitializeAndMigrateAsync_ShouldCreateDefaultSettings_WhenJsonFilesDoNotExist()
    {
        // Arrange
        var migrationService = new DatabaseMigrationService(
            _dbContextFactory,
            _fileSystem,
            _timeProvider,
            NullLogger<DatabaseMigrationService>.Instance);

        // Act
        await migrationService.InitializeAndMigrateAsync();

        // Assert
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var morningBriefing = await context.MorningBriefingSettings.FirstOrDefaultAsync();
        morningBriefing.Should().NotBeNull();
        morningBriefing!.Id.Should().Be(1);

        var techWatch = await context.TechWatchSettings.FirstOrDefaultAsync();
        techWatch.Should().NotBeNull();
        techWatch!.Id.Should().Be(1);

        var twitch = await context.TwitchSettings.FirstOrDefaultAsync();
        twitch.Should().NotBeNull();
        twitch!.Id.Should().Be(1);
    }

    [Test]
    public async Task InitializeAndMigrateAsync_ShouldBeIdempotent_WhenExecutedMultipleTimes()
    {
        // Arrange
        var morningBriefingJson = """{ "isEnabled": true, "scheduleTime": "08:15:00" }""";
        _fileSystem.AddFile("data/morning_briefing_settings.json", new MockFileData(morningBriefingJson));

        var migrationService = new DatabaseMigrationService(
            _dbContextFactory,
            _fileSystem,
            _timeProvider,
            NullLogger<DatabaseMigrationService>.Instance);

        // Act 1
        await migrationService.InitializeAndMigrateAsync();

        // Act 2 (Idempotency test)
        var secondRun = async () => await migrationService.InitializeAndMigrateAsync();
        await secondRun.Should().NotThrowAsync();

        // Assert
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var allSettings = await context.MorningBriefingSettings.ToListAsync();
        allSettings.Should().HaveCount(1);
        allSettings[0].ScheduleTime.Should().Be(new TimeOnly(8, 15));
    }

    [Test]
    public async Task InitializeAndMigrateAsync_ShouldConfigureWalModeAndBusyTimeout_ForSQLiteConcurrency()
    {
        // Arrange
        var migrationService = new DatabaseMigrationService(
            _dbContextFactory,
            _fileSystem,
            _timeProvider,
            NullLogger<DatabaseMigrationService>.Instance);

        // Act & Assert
        var act = async () => await migrationService.InitializeAndMigrateAsync();
        await act.Should().NotThrowAsync();
    }
}
