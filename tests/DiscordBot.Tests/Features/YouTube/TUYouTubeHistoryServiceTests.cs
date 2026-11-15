using DiscordBot.Features.YouTube;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Features.YouTube;

[TestFixture]
public class TUYouTubeHistoryServiceTests
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
    public void IsVideoSeen_ShouldReturnFalse_WhenVideoNotSeen()
    {
        // Arrange
        var service = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);

        // Act & Assert
        service.IsVideoSeen("unknown_vid").Should().BeFalse();
        service.IsVideoSeen("").Should().BeFalse();
        service.IsVideoSeen(null!).Should().BeFalse();
    }

    [Test]
    public void IsChannelInitialized_ShouldReturnFalse_WhenNotInitialized()
    {
        // Arrange
        var service = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);

        // Act & Assert
        service.IsChannelInitialized("UC_new_channel").Should().BeFalse();
        service.IsChannelInitialized("").Should().BeFalse();
        service.IsChannelInitialized(null!).Should().BeFalse();
    }

    [Test]
    public async Task InitializeChannelAsync_ShouldMarkChannelAndVideosAsSeen()
    {
        // Arrange
        var service = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);

        // Act
        await service.InitializeChannelAsync("UC12345", ["vid_1", "vid_2"]);

        // Assert
        service.IsChannelInitialized("UC12345").Should().BeTrue();
        service.IsVideoSeen("vid_1").Should().BeTrue();
        service.IsVideoSeen("vid_2").Should().BeTrue();
        service.IsVideoSeen("vid_3").Should().BeFalse();
    }

    [Test]
    public async Task MarkVideosAsSeenAsync_ShouldStoreVideoAndMarkChannel()
    {
        // Arrange
        var service = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);

        // Act
        await service.MarkVideosAsSeenAsync(["vid_abc", "vid_xyz"], "UC_creator_99");

        // Assert
        service.IsChannelInitialized("UC_creator_99").Should().BeTrue();
        service.IsVideoSeen("vid_abc").Should().BeTrue();
        service.IsVideoSeen("vid_xyz").Should().BeTrue();
        service.IsVideoSeen("vid_other").Should().BeFalse();
    }

    [Test]
    public async Task MarkVideosAsSeenAsync_ShouldPurgeVideosOlderThan90Days()
    {
        // Arrange
        var service = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);
        await service.MarkVideosAsSeenAsync(["old_video"], "UC_1");

        // Act: Advance time by 91 days
        _timeProvider.Advance(TimeSpan.FromDays(91));
        await service.MarkVideosAsSeenAsync(["new_video"], "UC_1");

        // Assert
        service.IsVideoSeen("old_video").Should().BeFalse();
        service.IsVideoSeen("new_video").Should().BeTrue();
    }

    [Test]
    public async Task PruneOldVideos_ShouldCapStoreAtMaxEntries()
    {
        // Arrange
        var service = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);

        // Act: Insert 5010 videos
        var batch1 = Enumerable.Range(1, 5010).Select(i => $"vid_{i:D5}").ToList();
        await service.MarkVideosAsSeenAsync(batch1, "UC_large");

        // Assert: Oldest 10 should be pruned
        service.IsVideoSeen("vid_00001").Should().BeFalse();
        service.IsVideoSeen("vid_00010").Should().BeFalse();
        service.IsVideoSeen("vid_00011").Should().BeTrue();
        service.IsVideoSeen("vid_05010").Should().BeTrue();
    }

    [Test]
    public async Task Persistence_ShouldReloadAcrossInstances()
    {
        // Arrange
        var service1 = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);
        await service1.InitializeChannelAsync("UC_persisted", ["persisted_vid"]);

        // Act: New instance using the same DbContextFactory
        var service2 = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);

        // Assert
        service2.IsChannelInitialized("UC_persisted").Should().BeTrue();
        service2.IsVideoSeen("persisted_vid").Should().BeTrue();
    }
}
