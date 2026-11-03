using System.IO.Abstractions.TestingHelpers;
using DiscordBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests;

[TestFixture]
public class SentArticlesHistoryServiceTests
{
    private MockFileSystem _fileSystem = null!;
    private FakeTimeProvider _timeProvider = null!;
    private readonly NullLogger<SentArticlesHistoryService> _logger = NullLogger<SentArticlesHistoryService>.Instance;

    [SetUp]
    public void SetUp()
    {
        _fileSystem = new MockFileSystem();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void IsAlreadySent_ShouldReturnFalse_WhenUrlWasNotMarked()
    {
        // Arrange
        var service = new SentArticlesHistoryService(_fileSystem, _timeProvider, _logger);

        // Act
        var result = service.IsAlreadySent("https://dev.to/article-1");

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public async Task MarkAsSentAsync_ShouldStoreUrlAndNormalize()
    {
        // Arrange
        var service = new SentArticlesHistoryService(_fileSystem, _timeProvider, _logger);

        // Act
        await service.MarkAsSentAsync(["https://DEV.TO/My-Article/"]);

        // Assert
        service.IsAlreadySent("https://dev.to/my-article").Should().BeTrue();
        service.IsAlreadySent("https://dev.to/my-article/").Should().BeTrue();
    }

    [Test]
    public async Task MarkAsSentAsync_ShouldPurgeArticlesOlderThan30Days()
    {
        // Arrange
        var service = new SentArticlesHistoryService(_fileSystem, _timeProvider, _logger);
        await service.MarkAsSentAsync(["https://dev.to/old-article"]);

        // Act : On avance le temps de 31 jours
        _timeProvider.Advance(TimeSpan.FromDays(31));
        await service.MarkAsSentAsync(["https://dev.to/new-article"]);

        // Assert
        service.IsAlreadySent("https://dev.to/new-article").Should().BeTrue();
        service.IsAlreadySent("https://dev.to/old-article").Should().BeFalse();
    }
}
