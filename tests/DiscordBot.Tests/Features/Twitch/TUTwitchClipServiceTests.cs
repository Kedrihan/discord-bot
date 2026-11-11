using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Discord.WebSocket;
using DiscordBot.DTO;
using DiscordBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests;

[TestFixture]
public class TUTwitchClipServiceTests
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
    public void FormatClipMessage_ShouldReplaceAllPlaceholders_CaseInsensitive()
    {
        // Arrange
        var template = "🎬 Nouveau clip de {STREAMER} par {CREATOR} : {TITLE} [{DURATION}] ({VIEWS} vues) -> {URL}";
        var clip = new TwitchClipInfoDto(
            Id: "InterestingMoment123",
            Url: "https://clips.twitch.tv/InterestingMoment123",
            EmbedUrl: "https://clips.twitch.tv/embed?clip=InterestingMoment123",
            BroadcasterId: "12345",
            BroadcasterName: "ZeratoR",
            CreatorId: "67890",
            CreatorName: "ClipGod",
            VideoId: "987654",
            GameId: "509658",
            Title: "Un fail monumental !",
            ViewCount: 4200,
            CreatedAt: new DateTimeOffset(2026, 11, 6, 11, 0, 0, TimeSpan.Zero),
            ThumbnailUrl: "https://clips.tv/thumb.jpg",
            Duration: 29.8
        );

        // Act
        var result = TwitchClipService.FormatClipMessage(template, clip);

        // Assert
        result.Should().Be("🎬 Nouveau clip de ZeratoR par ClipGod : Un fail monumental ! [30s] (4,200 vues) -> https://clips.twitch.tv/InterestingMoment123");
    }

    [Test]
    public void FormatClipMessage_ShouldReturnDefaultMessage_WhenTemplateIsNullOrWhitespace()
    {
        // Arrange
        var clip = new TwitchClipInfoDto(
            Id: "Clip456",
            Url: "https://clips.twitch.tv/Clip456",
            EmbedUrl: "https://clips.twitch.tv/embed?clip=Clip456",
            BroadcasterId: "123",
            BroadcasterName: "AntoineDaniel",
            CreatorId: "456",
            CreatorName: "FanNumberOne",
            VideoId: "",
            GameId: "",
            Title: "Clutch incroyable",
            ViewCount: 150,
            CreatedAt: _timeProvider.GetUtcNow(),
            ThumbnailUrl: "",
            Duration: 15.0
        );

        // Act
        var result = TwitchClipService.FormatClipMessage("", clip);

        // Assert
        result.Should().Contain("AntoineDaniel");
        result.Should().Contain("FanNumberOne");
        result.Should().Contain("https://clips.twitch.tv/Clip456");
    }

    [Test]
    public void FormatClipMessage_ShouldSanitizeMentionsInDynamicPlaceholders()
    {
        // Arrange
        var template = "{streamer} : {creator} - {title} -> {url}";
        var clip = new TwitchClipInfoDto(
            Id: "Clip789",
            Url: "https://clips.twitch.tv/Clip789",
            EmbedUrl: "",
            BroadcasterId: "1",
            BroadcasterName: "@everyone",
            CreatorId: "2",
            CreatorName: "@here",
            VideoId: "",
            GameId: "",
            Title: "Look <@123456789>",
            ViewCount: 1,
            CreatedAt: _timeProvider.GetUtcNow(),
            ThumbnailUrl: "",
            Duration: 10.0
        );

        // Act
        var result = TwitchClipService.FormatClipMessage(template, clip);

        // Assert
        result.Should().NotContain("@everyone");
        result.Should().NotContain("@here");
        result.Should().NotContain("<@123456789>");
        result.Should().Contain("@\u200beveryone");
        result.Should().Contain("@\u200bhere");
        result.Should().Contain("<\u200b@123456789>");
    }

    [Test]
    public void BuildClipEmbed_ShouldSetExpectedFieldsColorsAndTruncate()
    {
        // Arrange
        var longTitle = new string('A', 300);
        var longBroadcaster = new string('B', 300);
        var longCreator = new string('C', 300);

        var clip = new TwitchClipInfoDto(
            Id: "clip_embed_test",
            Url: "https://clips.twitch.tv/clip_embed_test",
            EmbedUrl: "",
            BroadcasterId: "100",
            BroadcasterName: longBroadcaster,
            CreatorId: "200",
            CreatorName: longCreator,
            VideoId: "999",
            GameId: "111",
            Title: longTitle,
            ViewCount: 9876,
            CreatedAt: new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero),
            ThumbnailUrl: "https://clips-media.twitch.tv/thumb.jpg",
            Duration: 45.4
        );

        // Act
        var embed = TwitchClipService.BuildClipEmbed(clip);

        // Assert
        embed.Color.Should().Be(TwitchLiveService.TwitchPurple);
        embed.Title.Should().HaveLength(256);
        embed.Title.Should().EndWith("...");
        embed.Author!.Value.Name.Should().HaveLength(256);
        embed.Author!.Value.Name.Should().EndWith("...");
        embed.Author!.Value.Url.Should().Contain("https://twitch.tv/");
        embed.Url.Should().Be("https://clips.twitch.tv/clip_embed_test");

        embed.Fields.Should().Contain(f => f.Name.Contains("Créé par") && f.Value.Length <= 256);
        embed.Fields.Should().Contain(f => f.Name.Contains("Durée") && f.Value == "45s");
        embed.Fields.Should().Contain(f => f.Name.Contains("Vues") && f.Value.Contains("9"));
        embed.Fields.Should().Contain(f => f.Name.Contains("Date") && f.Value.Contains(clip.CreatedAt.ToUnixTimeSeconds().ToString()));

        embed.Image.Should().NotBeNull();
        embed.Image!.Value.Url.Should().Be("https://clips-media.twitch.tv/thumb.jpg");
    }

    [Test]
    public void BuildClipEmbed_ShouldSanitizeMentionsInAllExternalFields()
    {
        // Arrange
        var clip = new TwitchClipInfoDto(
            Id: "security_test",
            Url: "https://clips.twitch.tv/security_test",
            EmbedUrl: "",
            BroadcasterId: "100",
            BroadcasterName: "@everyone Streamer",
            CreatorId: "200",
            CreatorName: "@here Clipper <@123456789>",
            VideoId: "",
            GameId: "",
            Title: "Title with @everyone and <@987654321>",
            ViewCount: 10,
            CreatedAt: _timeProvider.GetUtcNow(),
            ThumbnailUrl: "https://clips-media.twitch.tv/thumb.jpg",
            Duration: 15.0
        );

        // Act
        var embed = TwitchClipService.BuildClipEmbed(clip);

        // Assert
        embed.Title.Should().NotContain("@everyone");
        embed.Title.Should().NotContain("<@987654321>");
        embed.Title.Should().Contain("@\u200beveryone");
        embed.Title.Should().Contain("<\u200b@987654321>");

        embed.Author!.Value.Name.Should().NotContain("@everyone");
        embed.Author!.Value.Name.Should().Contain("@\u200beveryone");

        var creatorField = embed.Fields.First(f => f.Name.Contains("Créé par"));
        creatorField.Value.Should().NotContain("@here");
        creatorField.Value.Should().NotContain("<@123456789>");
        creatorField.Value.Should().Contain("@\u200bhere");
        creatorField.Value.Should().Contain("<\u200b@123456789>");
    }

    [Test]
    public void BuildClipEmbed_ShouldIgnoreNonHttpOrMalformedUrls()
    {
        // Arrange
        var clip = new TwitchClipInfoDto(
            Id: "url_test",
            Url: "javascript:alert('pwned')",
            EmbedUrl: "",
            BroadcasterId: "100",
            BroadcasterName: "Streamer",
            CreatorId: "200",
            CreatorName: "Creator",
            VideoId: "",
            GameId: "",
            Title: "Safe title",
            ViewCount: 1,
            CreatedAt: _timeProvider.GetUtcNow(),
            ThumbnailUrl: "ftp://unsafe/image.png",
            Duration: 10.0
        );

        // Act
        var embed = TwitchClipService.BuildClipEmbed(clip);

        // Assert
        embed.Url.Should().BeNull(); // Non-HTTP/HTTPS rejected
        embed.Image.Should().BeNull(); // Non-HTTP/HTTPS thumbnail rejected
    }

    [Test]
    public void FormatClipMessage_DefaultMessage_ShouldTruncateWhenExceeding2000Chars()
    {
        // Arrange
        var giantTitle = new string('X', 2500);
        var clip = new TwitchClipInfoDto(
            Id: "long_clip",
            Url: "https://clips.twitch.tv/long_clip",
            EmbedUrl: "",
            BroadcasterId: "1",
            BroadcasterName: giantTitle,
            CreatorId: "2",
            CreatorName: "Creator",
            VideoId: "",
            GameId: "",
            Title: "Test",
            ViewCount: 1,
            CreatedAt: _timeProvider.GetUtcNow(),
            ThumbnailUrl: "",
            Duration: 10.0
        );

        // Act
        var message = TwitchClipService.FormatClipMessage("", clip);

        // Assert
        message.Length.Should().Be(2000);
        message.Should().EndWith("...");
    }

    [Test]
    public async Task CheckNewClipsAsync_ShouldReturnDisabled_WhenGlobalTwitchDisabled()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto { IsEnabled = false, IsClipsEnabled = true });

        var historyService = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var apiClient = new TwitchApiClient(new HttpClient(httpHandler), configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        var clipService = new TwitchClipService(discordClient, configService, historyService, apiClient, _timeProvider, NullLogger<TwitchClipService>.Instance);

        // Act
        var result = await clipService.CheckNewClipsAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("désactivées");
    }

    [Test]
    public async Task CheckNewClipsAsync_ShouldReturnDisabled_WhenClipsDisabled()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto { IsEnabled = true, IsClipsEnabled = false });

        var historyService = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var apiClient = new TwitchApiClient(new HttpClient(httpHandler), configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        var clipService = new TwitchClipService(discordClient, configService, historyService, apiClient, _timeProvider, NullLogger<TwitchClipService>.Instance);

        // Act
        var result = await clipService.CheckNewClipsAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("clips");
    }

    [Test]
    public async Task CheckNewClipsAsync_ShouldReturnNoChannels_WhenNoChannelsConfiguredForClips()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            IsClipsEnabled = true,
            Channels =
            [
                new TrackedTwitchChannelDto
                {
                    TwitchUsername = "zerator",
                    IsEnabled = true,
                    IsClipEnabled = false // Clips disabled for this channel
                }
            ]
        });

        var historyService = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var apiClient = new TwitchApiClient(new HttpClient(httpHandler), configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        var clipService = new TwitchClipService(discordClient, configService, historyService, apiClient, _timeProvider, NullLogger<TwitchClipService>.Instance);

        // Act
        var result = await clipService.CheckNewClipsAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Aucune chaîne");
    }

    [Test]
    public async Task CheckNewClipsAsync_ColdStart_ShouldInitializeBroadcasterWithoutSendingMessages()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            IsClipsEnabled = true,
            ClientId = "test_client_id",
            ClientSecret = "test_client_secret",
            Channels =
            [
                new TrackedTwitchChannelDto
                {
                    TwitchUsername = "zerator",
                    IsEnabled = true,
                    IsClipEnabled = true,
                    ClipDiscordChannelId = "987654321098765432"
                }
            ]
        });

        var historyService = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());

        var httpHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("oauth2/token"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock_token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            if (uri.Contains("helix/users"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "id": "123456",
                                "login": "zerator",
                                "display_name": "ZeratoR",
                                "profile_image_url": "https://img.tv/zerator.png"
                            }
                        ]
                    }
                    """)
                };
            }

            if (uri.Contains("helix/clips"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "id": "ClipCold1",
                                "url": "https://clips.twitch.tv/ClipCold1",
                                "embed_url": "",
                                "broadcaster_id": "123456",
                                "broadcaster_name": "ZeratoR",
                                "creator_id": "999",
                                "creator_name": "Viewer1",
                                "video_id": "1",
                                "game_id": "2",
                                "title": "Old Clip 1",
                                "view_count": 50,
                                "created_at": "2026-11-06T10:00:00Z",
                                "thumbnail_url": "",
                                "duration": 15.0
                            },
                            {
                                "id": "ClipCold2",
                                "url": "https://clips.twitch.tv/ClipCold2",
                                "embed_url": "",
                                "broadcaster_id": "123456",
                                "broadcaster_name": "ZeratoR",
                                "creator_id": "888",
                                "creator_name": "Viewer2",
                                "video_id": "1",
                                "game_id": "2",
                                "title": "Old Clip 2",
                                "view_count": 80,
                                "created_at": "2026-11-06T11:00:00Z",
                                "thumbnail_url": "",
                                "duration": 20.0
                            }
                        ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var apiClient = new TwitchApiClient(new HttpClient(httpHandler), configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var clipService = new TwitchClipService(discordClient, configService, historyService, apiClient, _timeProvider, NullLogger<TwitchClipService>.Instance);

        // Broadcaster should not be initialized before check
        historyService.IsBroadcasterInitialized("123456").Should().BeFalse();

        // Act
        var result = await clipService.CheckNewClipsAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.ProcessedClipsCount.Should().Be(0); // Cold start skips sending messages
        historyService.IsBroadcasterInitialized("123456").Should().BeTrue();
        historyService.IsClipSeen("ClipCold1").Should().BeTrue();
        historyService.IsClipSeen("ClipCold2").Should().BeTrue();
    }

    [Test]
    public async Task TwitchApiClient_GetClipsAsync_ShouldQueryHelixClips_AndDeserializeProperly()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            ClientId = "helix_client_id",
            ClientSecret = "helix_secret"
        });

        var httpHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (uri.Contains("oauth2/token"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"valid_token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            if (uri.Contains("helix/clips"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "id": "HelixClip99",
                                "url": "https://clips.twitch.tv/HelixClip99",
                                "embed_url": "https://clips.twitch.tv/embed?clip=HelixClip99",
                                "broadcaster_id": "555",
                                "broadcaster_name": "StreamerOne",
                                "creator_id": "777",
                                "creator_name": "ClipperPro",
                                "video_id": "12345",
                                "game_id": "45678",
                                "title": "Action légendaire !",
                                "view_count": 3500,
                                "created_at": "2026-11-06T09:30:00Z",
                                "thumbnail_url": "https://clips.tv/preview.jpg",
                                "duration": 25.5
                            }
                        ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var apiClient = new TwitchApiClient(new HttpClient(httpHandler), configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        // Act
        var clips = await apiClient.GetClipsAsync("555", startedAt: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), first: 10);

        // Assert
        clips.Should().HaveCount(1);
        var clip = clips[0];
        clip.Id.Should().Be("HelixClip99");
        clip.Url.Should().Be("https://clips.twitch.tv/HelixClip99");
        clip.EmbedUrl.Should().Be("https://clips.twitch.tv/embed?clip=HelixClip99");
        clip.BroadcasterId.Should().Be("555");
        clip.BroadcasterName.Should().Be("StreamerOne");
        clip.CreatorId.Should().Be("777");
        clip.CreatorName.Should().Be("ClipperPro");
        clip.Title.Should().Be("Action légendaire !");
        clip.ViewCount.Should().Be(3500);
        clip.Duration.Should().Be(25.5);
    }

    [Test]
    public async Task TestClipNotificationAsync_ShouldReturnError_WhenChannelNotFound()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto { Channels = [] });

        var historyService = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var apiClient = new TwitchApiClient(new HttpClient(httpHandler), configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        var clipService = new TwitchClipService(discordClient, configService, historyService, apiClient, _timeProvider, NullLogger<TwitchClipService>.Instance);

        // Act
        var result = await clipService.TestClipNotificationAsync("unknown_streamer");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("introuvable");
    }

    [Test]
    public async Task TwitchClipScheduler_ShouldExecuteInitialDelayAndStopCleanly()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            IsClipsEnabled = true,
            ClipCheckIntervalSeconds = 60
        });

        var historyService = new TwitchClipHistoryService(_fileSystem, _timeProvider, NullLogger<TwitchClipHistoryService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var apiClient = new TwitchApiClient(new HttpClient(httpHandler), configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var clipService = new TwitchClipService(discordClient, configService, historyService, apiClient, _timeProvider, NullLogger<TwitchClipService>.Instance);

        var scheduler = new TwitchClipScheduler(configService, clipService, _timeProvider, NullLogger<TwitchClipScheduler>.Instance);

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

        // Advance past initial delay (10s)
        _timeProvider.Advance(TimeSpan.FromSeconds(11));

        // Cancel token to stop scheduler loop
        await cts.CancelAsync();
        await scheduler.StopAsync(CancellationToken.None);

        await executeTask;
        scheduler.IsWaitingForInitialDelay.Should().BeFalse();
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public MockHttpMessageHandler(string simpleJsonResponse)
        {
            _responseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(simpleJsonResponse)
            };
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
