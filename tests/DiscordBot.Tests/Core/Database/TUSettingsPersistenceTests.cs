using DiscordBot.DTO;
using DiscordBot.Services;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Core.Database;

[TestFixture]
public class TUSettingsPersistenceTests
{
    private TestBotDbContextFactory _dbContextFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _dbContextFactory = new TestBotDbContextFactory();
    }

    [TearDown]
    public void TearDown()
    {
        _dbContextFactory.Dispose();
    }

    [Test]
    public async Task MorningBriefingConfigService_SaveAndGetSettings_ShouldPersistInDatabaseAndCache()
    {
        // Arrange
        var service = new MorningBriefingConfigService(_dbContextFactory, NullLogger<MorningBriefingConfigService>.Instance);

        var originalSettings = await service.GetSettingsAsync();
        originalSettings.IsEnabled.Should().BeTrue();

        var updatedDto = new MorningBriefingSettingsDto
        {
            IsEnabled = true,
            ScheduleDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
            ScheduleTime = new TimeOnly(6, 45),
            DestinationType = DestinationTypeDto.UserDm,
            TargetId = "user-123456",
            Address = "Lyon, France",
            Latitude = 45.75,
            Longitude = 4.85,
            MorningStartHour = 6,
            MorningEndHour = 8,
            EveningStartHour = 16,
            EveningEndHour = 18
        };

        // Act
        await service.SaveSettingsAsync(updatedDto);

        // Assert: Cached retrieval
        var cached = service.GetSettings();
        cached.IsEnabled.Should().BeTrue();
        cached.ScheduleTime.Should().Be(new TimeOnly(6, 45));
        cached.Address.Should().Be("Lyon, France");
        cached.Latitude.Should().Be(45.75);

        // Assert: Fresh instance on same database
        var freshService = new MorningBriefingConfigService(_dbContextFactory, NullLogger<MorningBriefingConfigService>.Instance);
        var persisted = await freshService.GetSettingsAsync();
        persisted.IsEnabled.Should().BeTrue();
        persisted.ScheduleTime.Should().Be(new TimeOnly(6, 45));
        persisted.DestinationType.Should().Be(DestinationTypeDto.UserDm);
        persisted.TargetId.Should().Be("user-123456");
        persisted.Address.Should().Be("Lyon, France");
        persisted.ScheduleDays.Should().BeEquivalentTo([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday]);
    }

    [Test]
    public async Task TechWatchConfigService_SaveAndGetSettings_ShouldPersistInDatabaseAndCache()
    {
        // Arrange
        var service = new TechWatchConfigService(_dbContextFactory, NullLogger<TechWatchConfigService>.Instance);

        var originalSettings = await service.GetSettingsAsync();
        originalSettings.IsEnabled.Should().BeTrue();

        var updatedDto = new TechWatchSettingsDto
        {
            IsEnabled = true,
            ScheduleDays = [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            ScheduleTime = new TimeOnly(9, 0),
            DestinationType = DestinationTypeDto.Channel,
            TargetId = "chan-999",
            Topics = ["dotnet10", "csharp13", "sqlite"],
            EnableDevTo = true,
            EnableDotnetBlogRss = true,
            EnableHackerNews = false
        };

        // Act
        await service.SaveSettingsAsync(updatedDto);

        // Assert: Cached retrieval
        var cached = service.GetSettings();
        cached.IsEnabled.Should().BeTrue();
        cached.ScheduleTime.Should().Be(new TimeOnly(9, 0));
        cached.Topics.Should().BeEquivalentTo(["dotnet10", "csharp13", "sqlite"]);
        cached.EnableDevTo.Should().BeTrue();
        cached.EnableDotnetBlogRss.Should().BeTrue();
        cached.EnableHackerNews.Should().BeFalse();

        // Assert: Fresh instance on same database
        var freshService = new TechWatchConfigService(_dbContextFactory, NullLogger<TechWatchConfigService>.Instance);
        var persisted = await freshService.GetSettingsAsync();
        persisted.IsEnabled.Should().BeTrue();
        persisted.ScheduleTime.Should().Be(new TimeOnly(9, 0));
        persisted.Topics.Should().BeEquivalentTo(["dotnet10", "csharp13", "sqlite"]);
        persisted.EnableDevTo.Should().BeTrue();
        persisted.EnableHackerNews.Should().BeFalse();
    }

    [Test]
    public async Task TwitchConfigService_SaveAndGetSettings_ShouldSynchronizeTrackedChannels()
    {
        // Arrange
        var service = new TwitchConfigService(_dbContextFactory, logger: NullLogger<TwitchConfigService>.Instance);

        var initialDto = new TwitchSettingsDto
        {
            IsEnabled = true,
            CheckIntervalSeconds = 20,
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "streamer1", DiscordChannelId = "111", IsEnabled = true },
                new TrackedTwitchChannelDto { TwitchUsername = "streamer2", DiscordChannelId = "222", IsEnabled = false }
            ]
        };

        // Act 1: Initial save
        await service.SaveSettingsAsync(initialDto);

        var initialRetrieved = await service.GetSettingsAsync();
        initialRetrieved.Channels.Should().HaveCount(2);

        // Act 2: Update (remove streamer1, modify streamer2, add streamer3)
        var updatedDto = new TwitchSettingsDto
        {
            IsEnabled = true,
            CheckIntervalSeconds = 40,
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "streamer2", DiscordChannelId = "222-updated", IsEnabled = true },
                new TrackedTwitchChannelDto { TwitchUsername = "streamer3", DiscordChannelId = "333", IsEnabled = true }
            ]
        };
        await service.SaveSettingsAsync(updatedDto);

        // Assert: Fresh instance on same database
        var freshService = new TwitchConfigService(_dbContextFactory, logger: NullLogger<TwitchConfigService>.Instance);
        var persisted = await freshService.GetSettingsAsync();

        persisted.CheckIntervalSeconds.Should().Be(40);
        persisted.Channels.Should().HaveCount(2);
        persisted.Channels.Should().NotContain(c => c.TwitchUsername == "streamer1");
        persisted.Channels.Should().Contain(c => c.TwitchUsername == "streamer2" && c.DiscordChannelId == "222-updated" && c.IsEnabled);
        persisted.Channels.Should().Contain(c => c.TwitchUsername == "streamer3" && c.DiscordChannelId == "333" && c.IsEnabled);
    }
}
