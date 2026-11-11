using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Reflection;
using Discord.WebSocket;
using DiscordBot.Components.Pages;
using DiscordBot.DTO;
using DiscordBot.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests;

[TestFixture]
public class TUTwitchLiveServiceTests
{
    private MockFileSystem _fileSystem = null!;
    private FakeTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _fileSystem = new MockFileSystem();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 18, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void FormatMessage_ShouldReplaceAllPlaceholders_CaseInsensitive()
    {
        // Arrange
        var template = "🔴 Alerte : {STREAMER} est en direct sur {game} ! Titre: {Title} -> {url}";
        var streamer = "ZeratoR";
        var title = "Soirée Découverte de Jeux";
        var game = "Civilization VII";
        var url = "https://twitch.tv/zerator";

        // Act
        var result = TwitchLiveService.FormatMessage(template, streamer, title, game, url);

        // Assert
        result.Should().Be("🔴 Alerte : ZeratoR est en direct sur Civilization VII ! Titre: Soirée Découverte de Jeux -> https://twitch.tv/zerator");
    }

    [Test]
    public void FormatMessage_ShouldReturnDefaultMessage_WhenTemplateIsNullOrWhitespace()
    {
        // Act
        var result = TwitchLiveService.FormatMessage("", "Kameto", "Karmine Corp vs Vitality", "League of Legends", "https://twitch.tv/kamet0");

        // Assert
        result.Should().Contain("Kameto");
        result.Should().Contain("https://twitch.tv/kamet0");
    }

    [Test]
    public void BuildStreamEmbed_ShouldSetExpectedFieldsAndColors()
    {
        // Arrange
        var stream = new TwitchStreamInfoDto
        {
            StreamId = "stream-12345",
            UserId = "user-999",
            UserLogin = "zerator",
            UserName = "ZeratoR",
            GameName = "Trackmania",
            Title = "Entraînement Coupe du Monde",
            ViewerCount = 12500,
            StartedAt = new DateTimeOffset(2026, 11, 4, 17, 30, 0, TimeSpan.Zero),
            ThumbnailUrl = "https://static-cdn.jtvnw.net/previews-ttv/live_user_zerator-{width}x{height}.jpg"
        };
        var avatarUrl = "https://static-cdn.jtvnw.net/jtv_user_pictures/zerator-profile.png";

        // Act
        var embed = TwitchLiveService.BuildStreamEmbed(stream, avatarUrl);

        // Assert
        embed.Color.Should().Be(TwitchLiveService.TwitchPurple);
        embed.Title.Should().Be("Entraînement Coupe du Monde");
        embed.Url.Should().Be("https://twitch.tv/zerator");
        embed.Author.Should().NotBeNull();
        embed.Author!.Value.Name.Should().Be("ZeratoR");
        embed.Author!.Value.IconUrl.Should().Be(avatarUrl);

        embed.Fields.Should().Contain(f => f.Name.Contains("Jeu") && f.Value == "Trackmania");
        embed.Fields.Should().Contain(f => f.Name.Contains("Spectateurs") && f.Value.Contains("12"));
        embed.Fields.Should().Contain(f => f.Name.Contains("Début") && f.Value.Contains(stream.StartedAt.ToUnixTimeSeconds().ToString()));

        embed.Image.Should().NotBeNull();
        embed.Image!.Value.Url.Should().Be("https://static-cdn.jtvnw.net/previews-ttv/live_user_zerator-1280x720.jpg");
    }

    [Test]
    public async Task TwitchStateService_ShouldPersistAndRetrieveActiveStreams()
    {
        // Arrange
        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var state = new TwitchActiveStreamStateDto
        {
            TwitchUsername = "zerator",
            StreamId = "stream-100",
            DiscordChannelId = "1234567890",
            DiscordMessageId = 9876543210,
            StartedAt = _timeProvider.GetUtcNow()
        };

        // Act
        await stateService.SetActiveStreamAsync(state);
        var active = stateService.GetActiveStream("ZERATOR");
        var allActive = stateService.GetActiveStreams();

        // Assert
        active.Should().NotBeNull();
        active!.StreamId.Should().Be("stream-100");
        active.DiscordMessageId.Should().Be(9876543210);
        allActive.Should().HaveCount(1);

        // Act : Suppression
        await stateService.RemoveActiveStreamAsync("zerator");
        stateService.GetActiveStream("zerator").Should().BeNull();
        stateService.GetActiveStreams().Should().BeEmpty();
    }

    [Test]
    public void TwitchConfigService_ShouldFallbackToConfiguration_WhenSettingsEmpty()
    {
        // Arrange
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["TWITCH_CLIENT_ID"] = "env-client-id",
            ["TWITCH_CLIENT_SECRET"] = "env-client-secret",
            ["TWITCH_USER_ACCESS_TOKEN"] = "env-user-token"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();
        var configService = new TwitchConfigService(_fileSystem, configuration, NullLogger<TwitchConfigService>.Instance);

        // Act
        var settings = configService.GetSettings();

        // Assert
        settings.ClientId.Should().Be("env-client-id");
        settings.ClientSecret.Should().Be("env-client-secret");
        settings.UserAccessToken.Should().Be("env-user-token");
    }

    [Test]
    public async Task TwitchConfigService_SaveSettingsAsync_ShouldPersistToDisk()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, configuration, NullLogger<TwitchConfigService>.Instance);

        var newSettings = new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "custom-id",
            ClientSecret = "custom-secret",
            UserAccessToken = "custom-user-token-123",
            CheckIntervalSeconds = 45,
            Channels =
            [
                new TrackedTwitchChannelDto
                {
                    TwitchUsername = "teststreamer",
                    DiscordChannelId = "111222333"
                }
            ]
        };

        // Act
        await configService.SaveSettingsAsync(newSettings);

        // Assert
        var saved = configService.GetSettings();
        saved.ClientId.Should().Be("custom-id");
        saved.UserAccessToken.Should().Be("custom-user-token-123");
        saved.CheckIntervalSeconds.Should().Be(45);
        saved.Channels.Should().HaveCount(1);
        saved.Channels[0].TwitchUsername.Should().Be("teststreamer");
    }

    [Test]
    public void TwitchPage_ShouldHave_AdminOnly_AuthorizeAttribute_And_TwitchRoute()
    {
        var type = typeof(TwitchPage);

        var routeAttr = type.GetCustomAttribute<RouteAttribute>();
        routeAttr.Should().NotBeNull();
        routeAttr!.Template.Should().Be("/twitch");

        var authAttrs = type.GetCustomAttributes<AuthorizeAttribute>().ToList();
        authAttrs.Should().NotBeEmpty();
        authAttrs.Should().Contain(a => a.Policy == "AdminOnly");
    }

    [Test]
    public async Task CheckStreamsAsync_ShouldReturnDisabled_WhenTwitchFeatureDisabled()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto { IsEnabled = false });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        // Act
        var result = await liveService.CheckStreamsAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("désactivées");
    }

    [Test]
    public async Task CheckStreamsAsync_ShouldReturnNoChannels_WhenChannelListIsEmpty()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto { IsEnabled = true, Channels = [] });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        // Act
        var result = await liveService.CheckStreamsAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Aucune chaîne");
    }

    [Test]
    public async Task CheckStreamsAsync_ShouldCleanOfflineStreams_WhenStreamerNoLongerOnline()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "mock-client-id",
            ClientSecret = "mock-client-secret",
            Channels =
            [
                new TrackedTwitchChannelDto
                {
                    TwitchUsername = "streamer1",
                    DiscordChannelId = "123456789"
                }
            ]
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        // Prédéfinir le streamer comme étant en ligne
        await stateService.SetActiveStreamAsync(new TwitchActiveStreamStateDto
        {
            TwitchUsername = "streamer1",
            StreamId = "stream-previous",
            DiscordChannelId = "123456789",
            DiscordMessageId = 444555666,
            StartedAt = _timeProvider.GetUtcNow()
        });

        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());

        // Mock HTTP qui renvoie token valide puis liste vide de streams (le streamer est passé hors-ligne)
        var httpHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            // Streams endpoint : vide
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data": []}""")
            };
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        // Act
        var result = await liveService.CheckStreamsAsync();

        // Assert
        result.Success.Should().BeTrue();
        stateService.GetActiveStream("streamer1").Should().BeNull();
        stateService.GetActiveStreams().Should().BeEmpty();
    }

    [Test]
    public void SanitizeUntrustedText_ShouldNeutralizeMentions()
    {
        // Arrange
        var input = "Alerte @everyone et @here avec mention de rôle <@&123456789> !";

        // Act
        var sanitized = TwitchLiveService.SanitizeUntrustedText(input);

        // Assert
        sanitized.Should().NotContain("@everyone");
        sanitized.Should().NotContain("@here");
        sanitized.Should().NotContain("<@&");
        sanitized.Should().Contain("@\u200beveryone");
        sanitized.Should().Contain("@\u200bhere");
        sanitized.Should().Contain("<\u200b@&123456789>");
    }

    [Test]
    public void FormatMessage_ShouldSanitizeMentionsInDynamicPlaceholders()
    {
        // Arrange
        var template = "{streamer} joue à {game}: {title} sur {url}";
        var streamer = "Attacker @everyone";
        var title = "Free Nitro @here !";
        var game = "Scam <@123456>";
        var url = "https://twitch.tv/attacker";

        // Act
        var formatted = TwitchLiveService.FormatMessage(template, streamer, title, game, url);

        // Assert
        formatted.Should().NotContain("@everyone");
        formatted.Should().NotContain("@here");
        formatted.Should().NotContain("<@123456>");
        formatted.Should().Contain("@\u200beveryone");
        formatted.Should().Contain("@\u200bhere");
        formatted.Should().Contain("<\u200b@123456>");
    }

    [Test]
    public void BuildStreamEmbed_ShouldDefensivelyTruncate_VeryLongStrings()
    {
        // Arrange
        var longTitle = new string('A', 300);
        var longGame = new string('B', 300);
        var longName = new string('C', 300);
        var stream = new TwitchStreamInfoDto
        {
            StreamId = "stream-long",
            UserId = "user-1",
            UserLogin = "longuser",
            UserName = longName,
            GameName = longGame,
            Title = longTitle,
            ViewerCount = 10,
            StartedAt = DateTimeOffset.UtcNow,
            ThumbnailUrl = "https://static-cdn.jtvnw.net/previews-ttv/test-{width}x{height}.jpg"
        };

        // Act
        var embed = TwitchLiveService.BuildStreamEmbed(stream, "javascript:alert(1)");

        // Assert
        embed.Title.Length.Should().BeLessThanOrEqualTo(256);
        embed.Title.Should().EndWith("...");
        embed.Author!.Value.Name.Length.Should().BeLessThanOrEqualTo(256);
        embed.Author!.Value.Name.Should().EndWith("...");
        embed.Author!.Value.IconUrl.Should().BeNull(); // non-http URL should be rejected

        var gameField = embed.Fields.First(f => f.Name.Contains("Jeu"));
        gameField.Value.Length.Should().BeLessThanOrEqualTo(256);
        gameField.Value.Should().EndWith("...");
    }

    [Test]
    public async Task CheckStreamsAsync_ShouldPreventConcurrentExecution()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "id",
            ClientSecret = "secret",
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "slowchannel", DiscordChannelId = "123" }
            ]
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        var httpHandler = new MockHttpMessageHandler(async _ =>
        {
            return await tcs.Task;
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        // Act
        var firstRunTask = liveService.CheckStreamsAsync();

        // Second call while first is blocked waiting for HTTP
        var secondRunResult = await liveService.CheckStreamsAsync();

        // Release first call
        tcs.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"token","expires_in":3600,"token_type":"bearer"}""")
        });

        await firstRunTask;

        // Assert
        secondRunResult.Success.Should().BeFalse();
        secondRunResult.Message.Should().Contain("déjà en cours");
    }

    [Test]
    public async Task RefreshLiveStatusAsync_ShouldUpdateCache_And_TriggerOnLiveStatusChanged()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "zerator", DiscordChannelId = "123" },
                new TrackedTwitchChannelDto { TwitchUsername = "kamet0", DiscordChannelId = "456" }
            ]
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());

        var isStreamLive = true;
        var httpHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            if (isStreamLive)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "id": "stream-101",
                                "user_id": "user-zerator",
                                "user_login": "zerator",
                                "user_name": "ZeratoR",
                                "game_name": "Trackmania",
                                "title": "Live Cup",
                                "viewer_count": 5000,
                                "started_at": "2026-11-04T18:00:00Z",
                                "thumbnail_url": "https://previews/test-{width}x{height}.jpg"
                            }
                        ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data": []}""")
            };
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        var eventCount = 0;
        liveService.OnLiveStatusChanged += () => eventCount++;

        // Initial check before refresh
        liveService.IsStreamLive("ZERATOR").Should().BeFalse();
        liveService.GetLiveStreamInfo("zerator").Should().BeNull();
        liveService.GetLiveStreams().Should().BeEmpty();

        // Act 1: Stream ZeratoR est en direct
        await liveService.RefreshLiveStatusAsync();

        // Assert 1
        eventCount.Should().Be(1);
        liveService.IsStreamLive("ZERATOR").Should().BeTrue();
        liveService.IsStreamLive("zerator").Should().BeTrue();
        liveService.IsStreamLive("kamet0").Should().BeFalse();
        var streamInfo = liveService.GetLiveStreamInfo("zerator");
        streamInfo.Should().NotBeNull();
        streamInfo!.Title.Should().Be("Live Cup");
        streamInfo.ViewerCount.Should().Be(5000);
        liveService.GetLiveStreams().Should().HaveCount(1);

        // Act 2: Streamer passe hors-ligne
        isStreamLive = false;
        await liveService.RefreshLiveStatusAsync();

        // Assert 2
        eventCount.Should().Be(2);
        liveService.IsStreamLive("zerator").Should().BeFalse();
        liveService.GetLiveStreamInfo("zerator").Should().BeNull();
        liveService.GetLiveStreams().Should().BeEmpty();
    }

    [Test]
    public async Task CheckStreamsAsync_ShouldUpdateLiveStreamsCache_EvenForDisabledChannels()
    {
        // Arrange: 1 channel enabled for Discord, 1 channel disabled for Discord
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "activeStreamer", DiscordChannelId = "111", IsEnabled = true },
                new TrackedTwitchChannelDto { TwitchUsername = "disabledStreamer", DiscordChannelId = "222", IsEnabled = false }
            ]
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());

        var httpHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            // Both streamers returned as live
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                    "data": [
                        {
                            "id": "s1",
                            "user_id": "u1",
                            "user_login": "activestreamer",
                            "user_name": "ActiveStreamer",
                            "game_name": "Game 1",
                            "title": "Title 1",
                            "viewer_count": 100,
                            "started_at": "2026-11-04T18:00:00Z",
                            "thumbnail_url": "thumb"
                        },
                        {
                            "id": "s2",
                            "user_id": "u2",
                            "user_login": "disabledstreamer",
                            "user_name": "DisabledStreamer",
                            "game_name": "Game 2",
                            "title": "Title 2",
                            "viewer_count": 200,
                            "started_at": "2026-11-04T18:00:00Z",
                            "thumbnail_url": "thumb"
                        }
                    ]
                }
                """)
            };
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        var eventFired = false;
        liveService.OnLiveStatusChanged += () => eventFired = true;

        // Act
        var result = await liveService.CheckStreamsAsync();

        // Assert
        result.Success.Should().BeTrue();
        eventFired.Should().BeTrue();
        liveService.IsStreamLive("activestreamer").Should().BeTrue();
        liveService.IsStreamLive("disabledstreamer").Should().BeTrue();
        liveService.GetLiveStreams().Should().HaveCount(2);
    }

    [Test]
    public async Task RefreshLiveStatusAsync_ShouldIsolateFailingSubscribers_AndNotifyRemaining()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "test-id",
            ClientSecret = "test-secret",
            Channels = [new TrackedTwitchChannelDto { TwitchUsername = "streamer", DiscordChannelId = "123" }]
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());

        var httpHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data": []}""")
            };
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        var firstFailed = false;
        var secondSucceeded = false;

        liveService.OnLiveStatusChanged += () =>
        {
            firstFailed = true;
            throw new InvalidOperationException("Subscriber simulated failure");
        };

        liveService.OnLiveStatusChanged += () =>
        {
            secondSucceeded = true;
        };

        // Act - should not throw
        var act = async () => await liveService.RefreshLiveStatusAsync();
        await act.Should().NotThrowAsync();

        // Assert
        firstFailed.Should().BeTrue();
        secondSucceeded.Should().BeTrue();
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responseFactory;

        public MockHttpMessageHandler(string simpleJsonResponse)
        {
            _responseFactory = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(simpleJsonResponse)
            });
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = req => Task.FromResult(responseFactory(req));
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncResponseFactory)
        {
            _responseFactory = asyncResponseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _responseFactory(request);
        }
    }
}
