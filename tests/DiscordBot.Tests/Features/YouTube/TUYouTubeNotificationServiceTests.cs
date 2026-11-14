using System.Net;
using Discord.WebSocket;
using DiscordBot.DTO;
using DiscordBot.Features.YouTube;
using DiscordBot.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Features.YouTube;

[TestFixture]
public class TUYouTubeNotificationServiceTests
{
    private TestBotDbContextFactory _dbContextFactory = null!;
    private FakeTimeProvider _timeProvider = null!;

    private const string SampleFeedXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns:media="http://search.yahoo.com/mrss/">
          <link rel="self" href="http://www.youtube.com/feeds/videos.xml?channel_id=UC1234567890abcdefghijk"/>
          <id>yt:channel:UC1234567890abcdefghijk</id>
          <yt:channelId>UC1234567890abcdefghijk</yt:channelId>
          <title>Underscore_</title>
          <link rel="alternate" href="https://www.youtube.com/channel/UC1234567890abcdefghijk"/>
          <author>
            <name>Underscore_</name>
            <uri>https://www.youtube.com/channel/UC1234567890abcdefghijk</uri>
          </author>
          <published>2026-11-01T12:00:00+00:00</published>
          <entry>
            <id>yt:video:vid12345678</id>
            <yt:videoId>vid12345678</yt:videoId>
            <yt:channelId>UC1234567890abcdefghijk</yt:channelId>
            <title>Le meilleur épisode de la saison !</title>
            <link rel="alternate" href="https://www.youtube.com/watch?v=vid12345678"/>
            <author>
              <name>Underscore_</name>
              <uri>https://www.youtube.com/channel/UC1234567890abcdefghijk</uri>
            </author>
            <published>2026-11-14T08:00:00+00:00</published>
            <updated>2026-11-14T08:05:00+00:00</updated>
            <media:group>
              <media:title>Le meilleur épisode de la saison !</media:title>
              <media:content url="https://www.youtube.com/v/vid12345678?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
              <media:thumbnail url="https://i.ytimg.com/vi/vid12345678/hqdefault.jpg" width="480" height="360"/>
              <media:description>Une superbe vidéo complète.</media:description>
            </media:group>
          </entry>
        </feed>
        """;

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
    public void SanitizeUntrustedText_ShouldNeutralizeMentions()
    {
        YouTubeNotificationService.SanitizeUntrustedText("Hello @everyone and @here and <@123456>")
            .Should().Be("Hello @\u200beveryone and @\u200bhere and <\u200b@123456>");
        YouTubeNotificationService.SanitizeUntrustedText("").Should().BeEmpty();
    }

    [Test]
    public void SanitizeUntrustedText_ShouldNeutralizeRolesChannelsAndZeroWidthEvasions()
    {
        // Role mention and channel mention
        YouTubeNotificationService.SanitizeUntrustedText("Check <@&987654321> in <#11223344>")
            .Should().Be("Check <\u200b@&987654321> in <\u200b#11223344>");

        // Slash command mention
        YouTubeNotificationService.SanitizeUntrustedText("Run </help:123456>")
            .Should().Be("Run <\u200b/help:123456>");

        // Zero-width space / joiner obfuscation of @everyone / @here
        YouTubeNotificationService.SanitizeUntrustedText("Hey @\u200beveryone and @\u200dhere and @\uFEFFeveryone")
            .Should().Be("Hey @\u200beveryone and @\u200bhere and @\u200beveryone");
    }

    [Test]
    public void FormatMessage_ShouldPreventRecursiveTemplateInjection()
    {
        // Arrange - Title contains a placeholder {url} and channel contains {title}
        var video = new YouTubeVideoInfoDto(
            VideoId: "vid1",
            ChannelId: "UC1",
            ChannelTitle: "Channel with {title}",
            Title: "Injected {url} in title",
            Url: "https://www.youtube.com/watch?v=vid1",
            ThumbnailUrl: "https://img.yt/1.jpg",
            PublishedAt: DateTimeOffset.UtcNow,
            VideoType: "Vidéo"
        );

        // Act - In single-pass replacement, {title} inside channel title is NOT evaluated again
        var formatted = YouTubeNotificationService.FormatMessage("Video by {channel}!", video);

        // Assert
        formatted.Should().Be("Video by Channel with {title}!");
    }

    [Test]
    public void BuildAllowedMentions_ShouldOnlyPermitConfiguredMentions()
    {
        // Default / empty template -> None
        YouTubeNotificationService.BuildAllowedMentions(null).AllowedTypes
            .Should().BeNull();
        YouTubeNotificationService.BuildAllowedMentions("").AllowedTypes
            .Should().BeNull();
        YouTubeNotificationService.BuildAllowedMentions("🎥 Nouvelle vidéo de {channel}").AllowedTypes
            .Should().BeNull();

        // Explicit @everyone in template
        YouTubeNotificationService.BuildAllowedMentions("Hey @everyone !")
            .AllowedTypes.Should().Be(Discord.AllowedMentionTypes.Everyone);

        // Explicit role mention in template
        YouTubeNotificationService.BuildAllowedMentions("Hey <@&12345> !")
            .AllowedTypes.Should().Be(Discord.AllowedMentionTypes.Roles);
    }

    [Test]
    public void FormatMessage_ShouldReplaceAllPlaceholders_CaseInsensitive()
    {
        // Arrange
        var video = new YouTubeVideoInfoDto(
            VideoId: "vid1",
            ChannelId: "UC1",
            ChannelTitle: "Underscore_",
            Title: "Nouvel Épisode",
            Url: "https://www.youtube.com/watch?v=vid1",
            ThumbnailUrl: "https://img.yt/1.jpg",
            PublishedAt: DateTimeOffset.UtcNow,
            VideoType: "Vidéo"
        );

        var template = "[{TYPE}] **{CHANNEL}** a sorti : {TITLE} ! {URL}";

        // Act
        var formatted = YouTubeNotificationService.FormatMessage(template, video);

        // Assert
        formatted.Should().Be("[Vidéo] **Underscore_** a sorti : Nouvel Épisode ! https://www.youtube.com/watch?v=vid1");
    }

    [Test]
    public void FormatMessage_ShouldSanitizeMentionsInDynamicPlaceholders()
    {
        // Arrange
        var video = new YouTubeVideoInfoDto(
            VideoId: "vid1",
            ChannelId: "UC1",
            ChannelTitle: "Pirate @everyone",
            Title: "Hack <@9999>",
            Url: "https://www.youtube.com/watch?v=vid1",
            ThumbnailUrl: "https://img.yt/1.jpg",
            PublishedAt: DateTimeOffset.UtcNow,
            VideoType: "Vidéo"
        );

        // Act
        var formatted = YouTubeNotificationService.FormatMessage("{channel} - {title}", video);

        // Assert
        formatted.Should().NotContain("@everyone");
        formatted.Should().Contain("@\u200beveryone");
        formatted.Should().NotContain("<@");
        formatted.Should().Contain("<\u200b@");
    }

    [Test]
    public void FormatMessage_ShouldTruncateWhenExceeding2000Chars()
    {
        // Arrange
        var video = new YouTubeVideoInfoDto(
            VideoId: "vid1",
            ChannelId: "UC1",
            ChannelTitle: "Channel",
            Title: new string('A', 2500),
            Url: "https://www.youtube.com/watch?v=vid1",
            ThumbnailUrl: "https://img.yt/1.jpg",
            PublishedAt: DateTimeOffset.UtcNow,
            VideoType: "Vidéo"
        );

        // Act
        var formatted = YouTubeNotificationService.FormatMessage("{title}", video);

        // Assert
        formatted.Length.Should().Be(2000);
        formatted.Should().EndWith("...");
    }

    [Test]
    public void BuildVideoEmbed_ShouldSetExpectedFieldsColorsAndTruncate()
    {
        // Arrange
        var publishedDate = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
        var video = new YouTubeVideoInfoDto(
            VideoId: "vid1",
            ChannelId: "UC12345",
            ChannelTitle: new string('C', 300),
            Title: new string('T', 300),
            Url: "https://www.youtube.com/watch?v=vid1",
            ThumbnailUrl: "https://img.yt/1.jpg",
            PublishedAt: publishedDate,
            VideoType: "Short"
        );

        // Act
        var embed = YouTubeNotificationService.BuildVideoEmbed(video);

        // Assert
        embed.Color.Should().Be(YouTubeNotificationService.YouTubeRed);
        embed.Title.Length.Should().Be(256);
        embed.Author!.Value.Name.Length.Should().Be(256);
        embed.Author!.Value.Url.Should().Be("https://www.youtube.com/channel/UC12345");
        embed.Url.Should().Be("https://www.youtube.com/watch?v=vid1");
        embed.Image!.Value.Url.Should().Be("https://img.yt/1.jpg");
        embed.Timestamp.Should().Be(publishedDate);
        embed.Footer!.Value.Text.Should().Be("YouTube");

        var typeField = embed.Fields.FirstOrDefault(f => f.Name.Contains("Type"));
        typeField.Should().NotBeNull();
        typeField!.Value.Should().Be("Short");
    }

    [Test]
    public async Task CheckNewVideosAsync_ShouldReturnDisabled_WhenYouTubeFeatureDisabled()
    {
        // Arrange
        var configService = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);
        await configService.SaveSettingsAsync(new YouTubeSettingsDto { IsEnabled = false });

        var historyService = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var feedClient = new YouTubeFeedClient(new HttpClient(new MockHttpMessageHandler("{}")), NullLogger<YouTubeFeedClient>.Instance);

        var service = new YouTubeNotificationService(discordClient, configService, historyService, feedClient, _timeProvider, NullLogger<YouTubeNotificationService>.Instance);

        // Act
        var result = await service.CheckNewVideosAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("désactivées");
    }

    [Test]
    public async Task CheckNewVideosAsync_ShouldReturnNoChannels_WhenChannelListIsEmpty()
    {
        // Arrange
        var configService = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);
        await configService.SaveSettingsAsync(new YouTubeSettingsDto { IsEnabled = true, Channels = [] });

        var historyService = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var feedClient = new YouTubeFeedClient(new HttpClient(new MockHttpMessageHandler("{}")), NullLogger<YouTubeFeedClient>.Instance);

        var service = new YouTubeNotificationService(discordClient, configService, historyService, feedClient, _timeProvider, NullLogger<YouTubeNotificationService>.Instance);

        // Act
        var result = await service.CheckNewVideosAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Aucune chaîne YouTube active");
    }

    [Test]
    public async Task CheckNewVideosAsync_ColdStart_ShouldInitializeChannelWithoutSendingMessages()
    {
        // Arrange
        var configService = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);
        await configService.SaveSettingsAsync(new YouTubeSettingsDto
        {
            IsEnabled = true,
            Channels =
            [
                new TrackedYouTubeChannelDto
                {
                    ChannelId = "UC1234567890abcdefghijk",
                    ChannelTitle = "Underscore_",
                    DiscordChannelId = "111222333444",
                    IsEnabled = true
                }
            ]
        });

        var historyService = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());

        var httpHandler = new MockHttpMessageHandler(SampleFeedXml);
        var feedClient = new YouTubeFeedClient(new HttpClient(httpHandler), NullLogger<YouTubeFeedClient>.Instance);

        var service = new YouTubeNotificationService(discordClient, configService, historyService, feedClient, _timeProvider, NullLogger<YouTubeNotificationService>.Instance);

        // Precondition
        historyService.IsChannelInitialized("UC1234567890abcdefghijk").Should().BeFalse();

        // Act
        var result = await service.CheckNewVideosAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.ProcessedVideosCount.Should().Be(0); // Cold start skips sending notifications
        historyService.IsChannelInitialized("UC1234567890abcdefghijk").Should().BeTrue();
        historyService.IsVideoSeen("vid12345678").Should().BeTrue();
    }

    [Test]
    public async Task TestNotificationAsync_ShouldReturnError_WhenChannelNotFound()
    {
        // Arrange
        var configService = new YouTubeConfigService(_dbContextFactory, NullLogger<YouTubeConfigService>.Instance);
        var historyService = new YouTubeHistoryService(_dbContextFactory, _timeProvider, NullLogger<YouTubeHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var feedClient = new YouTubeFeedClient(new HttpClient(new MockHttpMessageHandler("{}")), NullLogger<YouTubeFeedClient>.Instance);

        var service = new YouTubeNotificationService(discordClient, configService, historyService, feedClient, _timeProvider, NullLogger<YouTubeNotificationService>.Instance);

        // Act
        var result = await service.TestNotificationAsync("UC_unknown");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("n'a pas été trouvée");
    }

    private class MockHttpMessageHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
