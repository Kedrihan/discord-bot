using DiscordBot.DTO;
using DiscordBot.Features.YouTube;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Features.YouTube;

[TestFixture]
public class TUYouTubeConfigServiceTests
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
    public void GetSettings_ShouldReturnDefaultSettings_WhenDatabaseIsEmpty()
    {
        // Arrange
        var service = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);

        // Act
        var settings = service.GetSettings();

        // Assert
        settings.Should().NotBeNull();
        settings.IsEnabled.Should().BeTrue();
        settings.CheckIntervalMinutes.Should().Be(15);
        settings.Channels.Should().BeEmpty();
    }

    [Test]
    public async Task SaveSettingsAsync_ShouldPersistToDatabase_AndClampInterval()
    {
        // Arrange
        var service = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);
        var newSettings = new YouTubeSettingsDto
        {
            IsEnabled = true,
            CheckIntervalMinutes = 0, // Should be clamped to 1
            Channels =
            [
                new TrackedYouTubeChannelDto
                {
                    ChannelId = "UC123456789",
                    ChannelTitle = "Underscore_",
                    ChannelHandle = "@Underscore_",
                    DiscordChannelId = "123456789012345678",
                    CustomMessage = "Nouvelle vidéo : {url}",
                    IsEnabled = true
                }
            ]
        };

        // Act
        await service.SaveSettingsAsync(newSettings);

        // Assert
        var savedSettings = service.GetSettings();
        savedSettings.CheckIntervalMinutes.Should().Be(1); // Clamped
        savedSettings.Channels.Should().HaveCount(1);
        savedSettings.Channels[0].ChannelId.Should().Be("UC123456789");

        // Reload from a fresh service instance
        var freshService = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);
        var loadedSettings = await freshService.GetSettingsAsync();
        loadedSettings.Channels.Should().HaveCount(1);
        loadedSettings.Channels[0].ChannelTitle.Should().Be("Underscore_");
    }
}
