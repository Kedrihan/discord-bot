using System.IO.Abstractions.TestingHelpers;
using DiscordBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests;

[TestFixture]
public class TU_TwitchClipHistoryServiceTests
{
    private MockFileSystem _fileSystem = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _fileSystem = new MockFileSystem();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void IsClipSeen_ShouldReturnFalse_WhenClipNotSeen()
    {
        // Arrange
        var service = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);

        // Act & Assert
        service.IsClipSeen("unknown_clip").Should().BeFalse();
        service.IsClipSeen("").Should().BeFalse();
        service.IsClipSeen(null!).Should().BeFalse();
    }

    [Test]
    public void IsBroadcasterInitialized_ShouldReturnFalse_WhenNotInitialized()
    {
        // Arrange
        var service = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);

        // Act & Assert
        service.IsBroadcasterInitialized("broadcaster_123").Should().BeFalse();
        service.IsBroadcasterInitialized("").Should().BeFalse();
        service.IsBroadcasterInitialized(null!).Should().BeFalse();
    }

    [Test]
    public async Task InitializeBroadcasterAsync_ShouldMarkBroadcasterAndClipsAsSeen()
    {
        // Arrange
        var service = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);

        // Act
        await service.InitializeBroadcasterAsync("12345", ["clip_1", "clip_2"]);

        // Assert
        service.IsBroadcasterInitialized("12345").Should().BeTrue();
        service.IsClipSeen("clip_1").Should().BeTrue();
        service.IsClipSeen("clip_2").Should().BeTrue();
        service.IsClipSeen("clip_3").Should().BeFalse();
    }

    [Test]
    public async Task MarkClipsAsSeenAsync_ShouldStoreClipAndMarkBroadcaster()
    {
        // Arrange
        var service = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);

        // Act
        await service.MarkClipsAsSeenAsync(["clip_abc", "clip_xyz"], "streamer_99");

        // Assert
        service.IsBroadcasterInitialized("streamer_99").Should().BeTrue();
        service.IsClipSeen("clip_abc").Should().BeTrue();
        service.IsClipSeen("clip_xyz").Should().BeTrue();
        service.IsClipSeen("clip_other").Should().BeFalse();
    }

    [Test]
    public async Task MarkClipsAsSeenAsync_ShouldPurgeClipsOlderThan30Days()
    {
        // Arrange
        var service = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        await service.MarkClipsAsSeenAsync(["old_clip"], "broadcaster_1");

        // Act: Advance time by 31 days
        _timeProvider.Advance(TimeSpan.FromDays(31));
        await service.MarkClipsAsSeenAsync(["new_clip"], "broadcaster_1");

        // Assert
        service.IsClipSeen("old_clip").Should().BeFalse();
        service.IsClipSeen("new_clip").Should().BeTrue();
    }

    [Test]
    public async Task Persistence_ShouldReloadFromDiskAcrossInstances()
    {
        // Arrange
        var service1 = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        await service1.InitializeBroadcasterAsync("broadcaster_42", ["clip_persisted_1", "clip_persisted_2"]);

        // Act: Create new instance pointing to same file system
        var service2 = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);

        // Assert
        service2.IsBroadcasterInitialized("broadcaster_42").Should().BeTrue();
        service2.IsClipSeen("clip_persisted_1").Should().BeTrue();
        service2.IsClipSeen("clip_persisted_2").Should().BeTrue();
        service2.IsClipSeen("clip_unknown").Should().BeFalse();
    }

    [Test]
    public async Task PruneOldClips_ShouldCapStoreAtMaxEntries()
    {
        // Arrange
        var service = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        var largeBatch = Enumerable.Range(1, 5500).Select(i => $"clip_{i}").ToList();

        // Act
        await service.InitializeBroadcasterAsync("broadcaster_cap", largeBatch);

        // Assert: It should cap at 5000 items
        service.IsBroadcasterInitialized("broadcaster_cap").Should().BeTrue();
        service.IsClipSeen("clip_5500").Should().BeTrue(); // latest clips retained
    }
}
