using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text.Json;
using Discord.WebSocket;
using DiscordBot.DTO;
using DiscordBot.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TwitchLib.EventSub.Websockets;

namespace DiscordBot.Tests;

[TestFixture]
public class TUTwitchEventSubTests
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
    public async Task TwitchApiClient_GetUsersByLoginAsync_ShouldFetchAndReturnUsers_AndCacheUserIds()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        });

        var httpHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            if (request.RequestUri?.AbsolutePath.Contains("helix/users") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "id": "12345678",
                                "login": "zerator",
                                "display_name": "ZeratoR",
                                "profile_image_url": "https://static-cdn.jtvnw.net/zerator.png"
                            }
                        ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        // Act
        var users = await apiClient.GetUsersByLoginAsync(["zerator"]);
        var resolvedId = await apiClient.GetUserIdAsync("zerator");

        // Assert
        users.Should().HaveCount(1);
        users[0].Id.Should().Be("12345678");
        users[0].Login.Should().Be("zerator");
        users[0].DisplayName.Should().Be("ZeratoR");
        users[0].ProfileImageUrl.Should().Be("https://static-cdn.jtvnw.net/zerator.png");
        resolvedId.Should().Be("12345678");
    }

    [Test]
    public async Task TwitchApiClient_CreateEventSubSubscriptionAsync_ShouldPostCorrectPayload_AndReturnSubId()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret"
        });

        HttpRequestMessage? recordedRequest = null;
        string? recordedContent = null;

        var httpHandler = new MockHttpMessageHandler(async request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            if (request.RequestUri?.AbsolutePath.Contains("helix/eventsub/subscriptions") == true)
            {
                recordedRequest = request;
                recordedContent = await request.Content!.ReadAsStringAsync();

                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "id": "sub-uuid-12345",
                                "status": "enabled",
                                "type": "stream.online",
                                "version": "1",
                                "cost": 0
                            }
                        ],
                        "total": 1,
                        "total_cost": 0,
                        "max_total_cost": 10000
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        // Act
        var result = await apiClient.CreateEventSubSubscriptionAsync("session-xyz", "stream.online", "broadcaster-999");

        // Assert
        result.Success.Should().BeTrue();
        result.SubscriptionId.Should().Be("sub-uuid-12345");

        recordedRequest.Should().NotBeNull();
        recordedRequest!.Headers.GetValues("Client-Id").Should().Contain("my-client-id");
        recordedRequest.Headers.Authorization?.Parameter.Should().Be("mock-token");

        recordedContent.Should().NotBeNull();
        using var jsonDoc = JsonDocument.Parse(recordedContent!);
        var root = jsonDoc.RootElement;
        root.GetProperty("type").GetString().Should().Be("stream.online");
        root.GetProperty("condition").GetProperty("broadcaster_user_id").GetString().Should().Be("broadcaster-999");
        root.GetProperty("transport").GetProperty("method").GetString().Should().Be("websocket");
        root.GetProperty("transport").GetProperty("session_id").GetString().Should().Be("session-xyz");
    }

    [Test]
    public async Task TwitchApiClient_CreateEventSubSubscriptionAsync_ShouldUseUserAccessToken_WhenProvided()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret",
            UserAccessToken = "settings-user-token"
        });

        HttpRequestMessage? recordedRequest = null;
        var tokenRequested = false;

        var httpHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                tokenRequested = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            if (request.RequestUri?.AbsolutePath.Contains("helix/eventsub/subscriptions") == true)
            {
                recordedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent("""
                    {
                        "data": [{ "id": "sub-uuid-abc", "status": "enabled", "type": "stream.online", "version": "1", "cost": 0 }]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        // Act 1: without explicit token param -> uses settings.UserAccessToken
        var result1 = await apiClient.CreateEventSubSubscriptionAsync("session-1", "stream.online", "broadcaster-1");

        // Assert 1
        result1.Success.Should().BeTrue();
        tokenRequested.Should().BeFalse();
        recordedRequest.Should().NotBeNull();
        recordedRequest!.Headers.Authorization?.Parameter.Should().Be("settings-user-token");

        // Act 2: with explicit token param -> overrides settings.UserAccessToken
        recordedRequest = null;
        var result2 = await apiClient.CreateEventSubSubscriptionAsync("session-2", "stream.offline", "broadcaster-2", userAccessToken: "explicit-token");

        // Assert 2
        result2.Success.Should().BeTrue();
        tokenRequested.Should().BeFalse();
        recordedRequest.Should().NotBeNull();
        recordedRequest!.Headers.Authorization?.Parameter.Should().Be("explicit-token");
    }

    [Test]
    public async Task TwitchApiClient_CreateEventSubSubscriptionAsync_ShouldHandleConflict_Gracefully()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret"
        });

        var httpHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"error":"Conflict","status":409,"message":"subscription already exists"}""")
            };
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        // Act
        var result = await apiClient.CreateEventSubSubscriptionAsync("session-xyz", "stream.online", "broadcaster-999");

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().Contain("déjà active");
    }

    [Test]
    public async Task TwitchApiClient_DeleteEventSubSubscriptionAsync_ShouldSendDeleteRequest_AndReturnTrue()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret"
        });

        HttpRequestMessage? recordedRequest = null;
        var httpHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            recordedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);

        // Act
        var success = await apiClient.DeleteEventSubSubscriptionAsync("sub-uuid-12345");

        // Assert
        success.Should().BeTrue();
        recordedRequest.Should().NotBeNull();
        recordedRequest!.Method.Should().Be(HttpMethod.Delete);
        recordedRequest.RequestUri?.Query.Should().Contain("id=sub-uuid-12345");
    }

    [Test]
    public async Task HandleStreamOfflineAsync_ShouldRemoveActiveStreamAndCache()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());

        await stateService.SetActiveStreamAsync(new TwitchActiveStreamStateDto
        {
            TwitchUsername = "streamer1",
            StreamId = "stream-999",
            DiscordChannelId = "123456789",
            DiscordMessageId = 555666777,
            StartedAt = _timeProvider.GetUtcNow()
        });

        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        // Act
        await liveService.HandleStreamOfflineAsync("broadcaster-1", "streamer1");

        // Assert
        stateService.GetActiveStream("streamer1").Should().BeNull();
        liveService.IsStreamLive("streamer1").Should().BeFalse();
    }

    [Test]
    public async Task TwitchEventSubService_Status_ShouldBeDisconnected_Initially()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        var wsClient = new EventSubWebsocketClient(NullLoggerFactory.Instance);
        await using var eventSubService = new TwitchEventSubService(
            wsClient, configService, apiClient, liveService, NullLogger<TwitchEventSubService>.Instance);

        // Act & Assert
        eventSubService.Status.Should().Be(TwitchEventSubStatusDto.Disconnected);
        eventSubService.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task TwitchEventSubService_StartAsync_ShouldReturnFalse_WhenDisabled()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = false,
            ClientId = "id",
            ClientSecret = "secret"
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        var wsClient = new EventSubWebsocketClient(NullLoggerFactory.Instance);
        await using var eventSubService = new TwitchEventSubService(
            wsClient, configService, apiClient, liveService, NullLogger<TwitchEventSubService>.Instance);

        // Act
        var result = await eventSubService.StartAsync();

        // Assert
        result.Should().BeFalse();
        eventSubService.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task TwitchEventSubService_SubscribeAllActiveChannelsAsync_ShouldCreateSubscriptions()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "my-client",
            ClientSecret = "my-secret",
            Channels =
            [
                new TrackedTwitchChannelDto
                {
                    TwitchUsername = "streamer1",
                    DiscordChannelId = "111222333",
                    IsEnabled = true
                }
            ]
        });

        var subscriptionsCreated = new List<string>();

        var httpHandler = new MockHttpMessageHandler(async request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            if (request.RequestUri?.AbsolutePath.Contains("helix/users") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "data": [
                            {
                                "id": "user-444",
                                "login": "streamer1",
                                "display_name": "Streamer1",
                                "profile_image_url": "https://avatar.png"
                            }
                        ]
                    }
                    """)
                };
            }

            if (request.RequestUri?.AbsolutePath.Contains("helix/eventsub/subscriptions") == true)
            {
                var body = await request.Content!.ReadAsStringAsync();
                subscriptionsCreated.Add(body);
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent("""
                    {
                        "data": [{ "id": "sub-1", "status": "enabled", "type": "stream.online", "version": "1", "cost": 0 }]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        var wsClient = new EventSubWebsocketClient(NullLoggerFactory.Instance);
        // Injecter un sessionId simulé sur wsClient via reflection pour tester SubscribeAllActiveChannelsAsync
        var sessionProp = typeof(EventSubWebsocketClient).GetProperty("SessionId");
        var backingField = typeof(EventSubWebsocketClient).GetField("<SessionId>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        backingField?.SetValue(wsClient, "mock-session-id");

        await using var eventSubService = new TwitchEventSubService(
            wsClient, configService, apiClient, liveService, NullLogger<TwitchEventSubService>.Instance);

        // Act
        var count = await eventSubService.SubscribeAllActiveChannelsAsync();

        // Assert
        count.Should().Be(1);
        subscriptionsCreated.Should().HaveCount(2); // stream.online and stream.offline
        subscriptionsCreated.Should().Contain(s => s.Contains("stream.online") && s.Contains("user-444") && s.Contains("mock-session-id"));
        subscriptionsCreated.Should().Contain(s => s.Contains("stream.offline") && s.Contains("user-444") && s.Contains("mock-session-id"));
    }

    [Test]
    public async Task TwitchLiveScheduler_ShouldStartEventSubAndHandleShutdownCleanly()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Channels = []
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        var wsClient = new EventSubWebsocketClient(NullLoggerFactory.Instance);
        await using var eventSubService = new TwitchEventSubService(
            wsClient, configService, apiClient, liveService, NullLogger<TwitchEventSubService>.Instance);

        var scheduler = new TwitchLiveScheduler(configService, liveService, eventSubService, _timeProvider, NullLogger<TwitchLiveScheduler>.Instance);

        using var cts = new CancellationTokenSource();

        // Act
        await scheduler.StartAsync(cts.Token);

        // Advance time by 6 seconds to trigger initial startup delay
        _timeProvider.Advance(TimeSpan.FromSeconds(6));

        // Cancel
        cts.Cancel();
        await scheduler.StopAsync(CancellationToken.None);

        // Assert
        eventSubService.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task TwitchEventSubService_StartAsync_ShouldReturnFalse_WhenUserAccessTokenIsEmpty()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            UserAccessToken = string.Empty
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);
        var wsClient = new EventSubWebsocketClient(NullLoggerFactory.Instance);

        await using var eventSubService = new TwitchEventSubService(
            wsClient, configService, apiClient, liveService, NullLogger<TwitchEventSubService>.Instance);

        // Act
        var result = await eventSubService.StartAsync();

        // Assert
        result.Should().BeFalse();
        eventSubService.Status.Should().Be(TwitchEventSubStatusDto.Disconnected);
        eventSubService.IsConnected.Should().BeFalse();
    }

    [Test]
    public async Task TwitchLiveScheduler_ShouldExecutePolling_WhenEventSubIsDisconnected()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            UserAccessToken = string.Empty,
            CheckIntervalSeconds = 15,
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "zerator", DiscordChannelId = "123456789", IsEnabled = true }
            ]
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var streamCheckCount = 0;
        var requestsReceived = new List<string>();

        var httpHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri?.ToString() ?? "null";
            lock (requestsReceived)
            {
                requestsReceived.Add(uri);
            }

            if (request.RequestUri?.AbsolutePath.Contains("oauth2/token") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"mock-token","expires_in":3600,"token_type":"bearer"}""")
                };
            }

            if (request.RequestUri?.AbsolutePath.Contains("helix/streams") == true)
            {
                Interlocked.Increment(ref streamCheckCount);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[]}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);
        var wsClient = new EventSubWebsocketClient(NullLoggerFactory.Instance);

        await using var eventSubService = new TwitchEventSubService(
            wsClient, configService, apiClient, liveService, NullLogger<TwitchEventSubService>.Instance);

        var scheduler = new TwitchLiveScheduler(configService, liveService, eventSubService, _timeProvider, NullLogger<TwitchLiveScheduler>.Instance);

        using var cts = new CancellationTokenSource();

        // Act
        await scheduler.StartAsync(cts.Token);

        // Wait until ExecuteAsync starts and enters initial delay
        for (var i = 0; i < 50 && !scheduler.IsWaitingForInitialDelay; i++)
        {
            await Task.Delay(10);
        }

        // Advance 5 seconds (initial startup delay)
        _timeProvider.Advance(TimeSpan.FromSeconds(6));
        for (var i = 0; i < 50 && Volatile.Read(ref streamCheckCount) == 0; i++)
        {
            await Task.Delay(10);
        }

        var initialCheckCount = Volatile.Read(ref streamCheckCount);

        // Advance 15 seconds (check interval periodic timer tick)
        _timeProvider.Advance(TimeSpan.FromSeconds(16));
        for (var i = 0; i < 50 && Volatile.Read(ref streamCheckCount) <= initialCheckCount; i++)
        {
            await Task.Delay(10);
        }
        var postTickCheckCount = Volatile.Read(ref streamCheckCount);

        // Stop scheduler
        cts.Cancel();
        await scheduler.StopAsync(CancellationToken.None);

        // Assert
        initialCheckCount.Should().Be(1);
        postTickCheckCount.Should().BeGreaterThan(initialCheckCount);
    }

    [Test]
    public async Task TwitchEventSubService_DisposeAsync_ShouldCleanlyUnregisterAndBeIdempotent()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);
        var wsClient = new EventSubWebsocketClient(NullLoggerFactory.Instance);

        var eventSubService = new TwitchEventSubService(
            wsClient, configService, apiClient, liveService, NullLogger<TwitchEventSubService>.Instance);

        eventSubService.Initialize();

        // Act & Assert (Should not throw and should be idempotent)
        var act1 = async () => await eventSubService.DisposeAsync();
        await act1.Should().NotThrowAsync();

        var act2 = async () => await eventSubService.DisposeAsync();
        await act2.Should().NotThrowAsync();
    }

    [Test]
    public async Task TwitchEventSubService_SubscribeAllActiveChannelsAsync_WithDuplicateHelixUsers_ShouldDeduplicateGracefully()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "zerator", DiscordChannelId = "123456789", IsEnabled = true }
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

            if (request.RequestUri?.AbsolutePath.Contains("helix/users") == true)
            {
                // Simulate Helix returning duplicates for the same user
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                        "data": [
                            { "id": "12345", "login": "zerator", "display_name": "ZeratoR", "profile_image_url": "https://img.png" },
                            { "id": "12345", "login": "zerator", "display_name": "ZeratoR", "profile_image_url": "https://img.png" }
                        ]
                    }
                    """)
                };
            }

            if (request.RequestUri?.AbsolutePath.Contains("helix/eventsub/subscriptions") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[{"id":"sub-1"}]}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);
        var wsClient = new EventSubWebsocketClient(NullLoggerFactory.Instance);

        var backingField = typeof(EventSubWebsocketClient).GetField("<SessionId>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        backingField?.SetValue(wsClient, "mock-session-123");

        await using var eventSubService = new TwitchEventSubService(
            wsClient, configService, apiClient, liveService, NullLogger<TwitchEventSubService>.Instance);

        // Act
        var result = await eventSubService.SubscribeAllActiveChannelsAsync();

        // Assert
        result.Should().Be(1);
    }

    [Test]
    public async Task TwitchLiveService_CheckStreamsAsync_ShouldReconcileEventSubFallbackStreamId_WithoutDeletingMessage()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "zerator", DiscordChannelId = "123456789", IsEnabled = true }
            ]
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var now = _timeProvider.GetUtcNow();
        await stateService.SetActiveStreamAsync(new TwitchActiveStreamStateDto
        {
            TwitchUsername = "zerator",
            StreamId = $"es_12345_{now.ToUnixTimeSeconds()}",
            DiscordChannelId = "123456789",
            DiscordMessageId = 555666777,
            StartedAt = now
        });

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

            if (request.RequestUri?.AbsolutePath.Contains("helix/streams") == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                    {
                        "data": [
                            {
                                "id": "helix-stream-998877",
                                "user_id": "12345",
                                "user_login": "zerator",
                                "user_name": "ZeratoR",
                                "game_name": "Elden Ring",
                                "title": "Stream Chill",
                                "viewer_count": 5000,
                                "started_at": "{{now:yyyy-MM-ddTHH:mm:ssZ}}",
                                "thumbnail_url": "https://img.jpg"
                            }
                        ]
                    }
                    """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        // Act
        var result = await liveService.CheckStreamsAsync();

        // Assert
        result.Success.Should().BeTrue();
        var active = stateService.GetActiveStream("zerator");
        active.Should().NotBeNull();
        active!.StreamId.Should().Be("helix-stream-998877");
        active.DiscordMessageId.Should().Be(555666777); // DiscordMessageId must be preserved!
    }

    [Test]
    public async Task TwitchLiveService_HandleStreamOnlineAsync_ShouldNotDuplicate_WhenAlreadyNotifiedByPolling()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        var configService = new TwitchConfigService(_fileSystem, config, NullLogger<TwitchConfigService>.Instance);
        await configService.SaveSettingsAsync(new TwitchSettingsDto
        {
            IsEnabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Channels =
            [
                new TrackedTwitchChannelDto { TwitchUsername = "zerator", DiscordChannelId = "123456789", IsEnabled = true }
            ]
        });

        var stateService = new TwitchStateService(_fileSystem, NullLogger<TwitchStateService>.Instance);
        var now = _timeProvider.GetUtcNow();
        await stateService.SetActiveStreamAsync(new TwitchActiveStreamStateDto
        {
            TwitchUsername = "zerator",
            StreamId = "helix-stream-111",
            DiscordChannelId = "123456789",
            DiscordMessageId = 888999000,
            StartedAt = now
        });

        var discordClient = new DiscordSocketClient(new DiscordSocketConfig());
        var httpHandler = new MockHttpMessageHandler("{}");
        var httpClient = new HttpClient(httpHandler);
        var apiClient = new TwitchApiClient(httpClient, configService, _timeProvider, NullLogger<TwitchApiClient>.Instance);
        var liveService = new TwitchLiveService(discordClient, configService, stateService, apiClient, NullLogger<TwitchLiveService>.Instance);

        // Act: EventSub online arrives for a stream that is already notified by polling
        await liveService.HandleStreamOnlineAsync("12345", "zerator", "ZeratoR", now);

        // Assert: state remains the exact same active stream without re-posting
        var active = stateService.GetActiveStream("zerator");
        active.Should().NotBeNull();
        active!.StreamId.Should().Be("helix-stream-111");
        active.DiscordMessageId.Should().Be(888999000);
    }

    [Test]
    public void TwitchLiveService_MentionNeutralization_ShouldSanitizeMaliciousMentions()
    {
        // Arrange
        var untrustedStreamer = "Hacker @everyone";
        var untrustedTitle = "Free Nitro! @here join <@&123456789> or <@987654321>";
        var untrustedGame = "Game <@everyone>";
        var url = "https://twitch.tv/hacker";

        // Act
        var formatted = TwitchLiveService.FormatMessage(
            "🔴 {streamer} joue à {game} ! {title} ({url})",
            untrustedStreamer, untrustedTitle, untrustedGame, url);

        // Assert
        formatted.Should().NotContain("@everyone");
        formatted.Should().NotContain("@here");
        formatted.Should().NotContain("<@");
        formatted.Should().Contain("@\u200beveryone");
        formatted.Should().Contain("@\u200bhere");
        formatted.Should().Contain("<\u200b@");
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
