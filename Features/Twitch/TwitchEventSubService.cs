using DiscordBot.DTO;
using TwitchLib.EventSub.Core.EventArgs.Stream;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace DiscordBot.Services;

public class TwitchEventSubService(
    EventSubWebsocketClient websocketClient,
    TwitchConfigService configService,
    TwitchApiClient twitchApiClient,
    TwitchLiveService liveService,
    ILogger<TwitchEventSubService> logger,
    IConfiguration? configuration = null) : IAsyncDisposable
{
    private TwitchEventSubStatusDto _status = TwitchEventSubStatusDto.Disconnected;
    private readonly SemaphoreSlim _subLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public event Action? OnStatusChanged;

    public TwitchEventSubStatusDto Status => _status;
    public bool IsConnected => _status == TwitchEventSubStatusDto.Connected;
    public string? SessionId => websocketClient.SessionId;

    private string? GetClientId() => configuration?["TWITCH_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID") ?? twitchApiClient.GetClientId();
    private string? GetClientSecret() => configuration?["TWITCH_CLIENT_SECRET"] ?? Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET") ?? twitchApiClient.GetClientSecret();
    private string? GetUserAccessToken() => configuration?["TWITCH_USER_ACCESS_TOKEN"] ?? Environment.GetEnvironmentVariable("TWITCH_USER_ACCESS_TOKEN") ?? twitchApiClient.GetUserAccessToken();

    private bool HasCredentials => !string.IsNullOrWhiteSpace(GetClientId()) && !string.IsNullOrWhiteSpace(GetClientSecret());
    private bool HasUserAccessToken => !string.IsNullOrWhiteSpace(GetUserAccessToken());

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        websocketClient.WebsocketConnected += OnWebsocketConnectedAsync;
        websocketClient.WebsocketDisconnected += OnWebsocketDisconnectedAsync;
        websocketClient.WebsocketReconnected += OnWebsocketReconnectedAsync;
        websocketClient.ErrorOccurred += OnErrorOccurredAsync;
        websocketClient.StreamOnline += OnStreamOnlineAsync;
        websocketClient.StreamOffline += OnStreamOfflineAsync;
    }

    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        Initialize();

        var settings = configService.GetSettings();
        if (!HasCredentials)
        {
            logger.LogWarning("Les identifiants Twitch API (Client ID / Client Secret) ne sont pas configurés. WebSocket EventSub non démarré.");
            _status = TwitchEventSubStatusDto.Disconnected;
            NotifyStatusChanged();
            return false;
        }

        if (!settings.IsEnabled)
        {
            logger.LogInformation("Les notifications Twitch sont désactivées. WebSocket EventSub non démarré.");
            _status = TwitchEventSubStatusDto.Disconnected;
            NotifyStatusChanged();
            return false;
        }

        if (!HasUserAccessToken)
        {
            logger.LogInformation("Aucun User Access Token Twitch configuré. Le WebSocket EventSub requiert un jeton utilisateur. Mode polling automatique actif.");
            _status = TwitchEventSubStatusDto.Disconnected;
            NotifyStatusChanged();
            return false;
        }

        try
        {
            _status = TwitchEventSubStatusDto.Connecting;
            NotifyStatusChanged();

            logger.LogInformation("Connexion au WebSocket EventSub Twitch en cours...");
            var connected = await websocketClient.ConnectAsync();
            if (!connected)
            {
                logger.LogError("Échec de l'établissement de la connexion WebSocket EventSub.");
                _status = TwitchEventSubStatusDto.Error;
                NotifyStatusChanged();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du démarrage du client WebSocket EventSub.");
            _status = TwitchEventSubStatusDto.Error;
            NotifyStatusChanged();
            return false;
        }
    }

    public async Task<bool> StopAsync(CancellationToken ct = default)
    {
        try
        {
            var disconnected = await websocketClient.DisconnectAsync();
            _status = TwitchEventSubStatusDto.Disconnected;
            NotifyStatusChanged();
            return disconnected;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la déconnexion du WebSocket EventSub.");
            _status = TwitchEventSubStatusDto.Disconnected;
            NotifyStatusChanged();
            return false;
        }
    }

    public async Task<int> ResubscribeAllAsync(CancellationToken ct = default)
    {
        var settings = configService.GetSettings();
        if (!settings.IsEnabled || !HasCredentials || !HasUserAccessToken)
        {
            if (IsConnected)
            {
                await StopAsync(ct);
            }
            return 0;
        }

        if (!IsConnected)
        {
            await StartAsync(ct);
            return 0;
        }

        return await SubscribeAllActiveChannelsAsync(ct);
    }

    public async Task<int> SubscribeAllActiveChannelsAsync(CancellationToken ct = default)
    {
        var sessionId = websocketClient.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            logger.LogWarning("Impossible de souscrire aux événements EventSub : SessionId WebSocket non disponible.");
            return 0;
        }

        var settings = configService.GetSettings();
        if (!settings.IsEnabled)
        {
            logger.LogInformation("Notifications Twitch désactivées : aucune souscription créée.");
            return 0;
        }

        var activeChannels = settings.Channels
            .Where(c => c.IsEnabled && !string.IsNullOrWhiteSpace(c.TwitchUsername))
            .ToList();

        if (activeChannels.Count == 0)
        {
            logger.LogInformation("Aucune chaîne active configurée pour la souscription EventSub.");
            return 0;
        }

        await _subLock.WaitAsync(ct);
        try
        {
            var usernames = activeChannels
                .Select(c => c.TwitchUsername.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var users = await twitchApiClient.GetUsersByLoginAsync(usernames, ct);
            var userMap = users
                .DistinctBy(u => u.Login, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(u => u.Login, u => u.Id, StringComparer.OrdinalIgnoreCase);

            var successCount = 0;
            foreach (var channel in activeChannels)
            {
                if (!userMap.TryGetValue(channel.TwitchUsername.Trim(), out var userId))
                {
                    logger.LogWarning("Impossible de résoudre l'ID Twitch pour la chaîne {Username}.", channel.TwitchUsername);
                    continue;
                }

                var userToken = GetUserAccessToken();
                var onlineSub = await twitchApiClient.CreateEventSubSubscriptionAsync(
                    sessionId,
                    "stream.online",
                    userId,
                    userToken,
                    ct);
                var offlineSub = await twitchApiClient.CreateEventSubSubscriptionAsync(
                    sessionId,
                    "stream.offline",
                    userId,
                    userToken,
                    ct);

                if (onlineSub.Success && offlineSub.Success)
                {
                    successCount++;
                }
            }

            logger.LogInformation("Souscriptions EventSub configurées avec succès pour {Count}/{Total} chaînes.", successCount, activeChannels.Count);
            return successCount;
        }
        finally
        {
            _subLock.Release();
        }
    }

    private async Task OnWebsocketConnectedAsync(object? sender, WebsocketConnectedArgs args)
    {
        try
        {
            _status = TwitchEventSubStatusDto.Connected;
            logger.LogInformation("Connecté au WebSocket EventSub de Twitch. Reconnect: {IsRequestedReconnect}",
                args.IsRequestedReconnect);

            NotifyStatusChanged();

            if (!args.IsRequestedReconnect)
            {
                await SubscribeAllActiveChannelsAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur non gérée lors du traitement de WebsocketConnected.");
        }
    }

    private Task OnWebsocketDisconnectedAsync(object? sender, WebsocketDisconnectedArgs args)
    {
        try
        {
            _status = TwitchEventSubStatusDto.Disconnected;
            logger.LogWarning("Déconnecté du WebSocket EventSub de Twitch.");
            NotifyStatusChanged();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur non gérée lors du traitement de WebsocketDisconnected.");
        }
        return Task.CompletedTask;
    }

    private Task OnWebsocketReconnectedAsync(object? sender, WebsocketReconnectedArgs args)
    {
        try
        {
            _status = TwitchEventSubStatusDto.Connected;
            logger.LogInformation("Reconnecté au WebSocket EventSub de Twitch.");
            NotifyStatusChanged();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur non gérée lors du traitement de WebsocketReconnected.");
        }
        return Task.CompletedTask;
    }

    private Task OnErrorOccurredAsync(object? sender, ErrorOccuredArgs args)
    {
        try
        {
            logger.LogError(args.Exception, "Erreur sur le WebSocket EventSub : {Message}", args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur non gérée lors de la notification ErrorOccurred.");
        }
        return Task.CompletedTask;
    }

    private async Task OnStreamOnlineAsync(object? sender, StreamOnlineArgs args)
    {
        try
        {
            var ev = args.Payload?.Event;
            if (ev == null) return;

            logger.LogInformation("Événement EventSub StreamOnline reçu pour {Streamer} ({Login}, ID: {UserId}).",
                ev.BroadcasterUserName, ev.BroadcasterUserLogin, ev.BroadcasterUserId);

            await liveService.HandleStreamOnlineAsync(
                ev.BroadcasterUserId,
                ev.BroadcasterUserLogin,
                ev.BroadcasterUserName,
                ev.StartedAt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur non gérée lors du traitement de StreamOnline.");
        }
    }

    private async Task OnStreamOfflineAsync(object? sender, StreamOfflineArgs args)
    {
        try
        {
            var ev = args.Payload?.Event;
            if (ev == null) return;

            logger.LogInformation("Événement EventSub StreamOffline reçu pour {Streamer} ({Login}, ID: {UserId}).",
                ev.BroadcasterUserName, ev.BroadcasterUserLogin, ev.BroadcasterUserId);

            await liveService.HandleStreamOfflineAsync(
                ev.BroadcasterUserId,
                ev.BroadcasterUserLogin);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur non gérée lors du traitement de StreamOffline.");
        }
    }

    private void NotifyStatusChanged()
    {
        var handler = OnStatusChanged;
        if (handler == null) return;

        foreach (var receiver in handler.GetInvocationList())
        {
            try
            {
                ((Action)receiver)();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la notification OnStatusChanged d'EventSub.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_initialized)
        {
            websocketClient.WebsocketConnected -= OnWebsocketConnectedAsync;
            websocketClient.WebsocketDisconnected -= OnWebsocketDisconnectedAsync;
            websocketClient.WebsocketReconnected -= OnWebsocketReconnectedAsync;
            websocketClient.ErrorOccurred -= OnErrorOccurredAsync;
            websocketClient.StreamOnline -= OnStreamOnlineAsync;
            websocketClient.StreamOffline -= OnStreamOfflineAsync;
            _initialized = false;
        }

        OnStatusChanged = null;

        try
        {
            await StopAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'arrêt du WebSocket durant DisposeAsync.");
        }

        _subLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
