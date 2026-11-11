using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordBot.DTO;

namespace DiscordBot.Services;

public class TwitchApiClient(
    HttpClient httpClient,
    TwitchConfigService configService,
    TimeProvider timeProvider,
    ILogger<TwitchApiClient> logger,
    IConfiguration? configuration = null)
{
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly ConcurrentDictionary<string, (string Url, DateTimeOffset CachedAt)> _avatarCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _userIdCache = new(StringComparer.OrdinalIgnoreCase);

    public TwitchConfigService ConfigService => configService;

    public string? GetClientId() => configuration?["TWITCH_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID");
    public string? GetClientSecret() => configuration?["TWITCH_CLIENT_SECRET"] ?? Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET");
    public string? GetUserAccessToken() => configuration?["TWITCH_USER_ACCESS_TOKEN"] ?? Environment.GetEnvironmentVariable("TWITCH_USER_ACCESS_TOKEN");

    public bool HasCredentials => !string.IsNullOrWhiteSpace(GetClientId()) && !string.IsNullOrWhiteSpace(GetClientSecret());
    public bool HasUserAccessToken => !string.IsNullOrWhiteSpace(GetUserAccessToken());

    public async Task<IReadOnlyList<TwitchStreamInfoDto>> GetStreamsAsync(IEnumerable<string> userLogins, CancellationToken ct = default)
    {
        var logins = userLogins
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (logins.Count == 0) return [];

        var clientId = GetClientId();
        var clientSecret = GetClientSecret();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogWarning("Les identifiants de l'API Twitch (Client ID ou Client Secret) ne sont pas configurés.");
            return [];
        }

        var token = await GetValidAccessTokenAsync(clientId, clientSecret, ct);
        if (string.IsNullOrEmpty(token)) return [];

        var results = new List<TwitchStreamInfoDto>();

        // Helix limite à 100 requêtes de user_login par appel
        foreach (var chunk in logins.Chunk(100))
        {
            try
            {
                var queryParams = string.Join("&", chunk.Select(u => $"user_login={Uri.EscapeDataString(u.Trim())}"));
                var requestUri = $"https://api.twitch.tv/helix/streams?{queryParams}";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Add("Client-Id", clientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError("Échec de la récupération des streams Twitch Helix. Statut HTTP : {StatusCode}", response.StatusCode);
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _cachedToken = null;
                        _tokenExpiresAt = DateTimeOffset.MinValue;
                    }
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                var streamResponse = JsonSerializer.Deserialize<HelixStreamsResponse>(content);
                if (streamResponse?.Data == null) continue;

                foreach (var s in streamResponse.Data)
                {
                    results.Add(new TwitchStreamInfoDto
                    {
                        StreamId = s.Id,
                        UserId = s.UserId,
                        UserLogin = s.UserLogin,
                        UserName = s.UserName,
                        GameName = s.GameName,
                        Title = s.Title,
                        ViewerCount = s.ViewerCount,
                        StartedAt = s.StartedAt,
                        ThumbnailUrl = s.ThumbnailUrl
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la requête de streams Twitch.");
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<TwitchClipInfoDto>> GetClipsAsync(
        string broadcasterUserId,
        DateTimeOffset? startedAt = null,
        int first = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(broadcasterUserId)) return [];

        var clientId = GetClientId();
        var clientSecret = GetClientSecret();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogWarning("Les identifiants de l'API Twitch (Client ID ou Client Secret) ne sont pas configurés.");
            return [];
        }

        var token = await GetValidAccessTokenAsync(clientId, clientSecret, ct);
        if (string.IsNullOrEmpty(token)) return [];

        var clampedFirst = Math.Clamp(first, 1, 100);
        var query = $"broadcaster_id={Uri.EscapeDataString(broadcasterUserId.Trim())}&first={clampedFirst}";
        if (startedAt.HasValue)
        {
            query += $"&started_at={Uri.EscapeDataString(startedAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}";
        }

        var requestUri = $"https://api.twitch.tv/helix/clips?{query}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add("Client-Id", clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Échec de la récupération des clips Twitch Helix pour broadcaster {UserId}. Statut HTTP : {StatusCode}", broadcasterUserId, response.StatusCode);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _cachedToken = null;
                    _tokenExpiresAt = DateTimeOffset.MinValue;
                }
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var clipsResponse = JsonSerializer.Deserialize<HelixClipsResponse>(content);
            if (clipsResponse?.Data == null) return [];

            return clipsResponse.Data.Select(c => new TwitchClipInfoDto(
                Id: c.Id ?? string.Empty,
                Url: c.Url ?? string.Empty,
                EmbedUrl: c.EmbedUrl ?? string.Empty,
                BroadcasterId: c.BroadcasterId ?? string.Empty,
                BroadcasterName: c.BroadcasterName ?? string.Empty,
                CreatorId: c.CreatorId ?? string.Empty,
                CreatorName: c.CreatorName ?? string.Empty,
                VideoId: c.VideoId ?? string.Empty,
                GameId: c.GameId ?? string.Empty,
                Title: c.Title ?? string.Empty,
                ViewCount: c.ViewCount,
                CreatedAt: c.CreatedAt,
                ThumbnailUrl: c.ThumbnailUrl ?? string.Empty,
                Duration: c.Duration
            )).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la récupération des clips Twitch pour broadcaster {UserId}.", broadcasterUserId);
            return [];
        }
    }

    public async Task<IReadOnlyList<TwitchUserInfoDto>> GetUsersByLoginAsync(IEnumerable<string> userLogins, CancellationToken ct = default)
    {
        var logins = userLogins
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (logins.Count == 0) return [];

        var clientId = GetClientId();
        var clientSecret = GetClientSecret();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogWarning("Les identifiants de l'API Twitch (Client ID ou Client Secret) ne sont pas configurés.");
            return [];
        }

        var token = await GetValidAccessTokenAsync(clientId, clientSecret, ct);
        if (string.IsNullOrEmpty(token)) return [];

        var results = new List<TwitchUserInfoDto>();
        var now = timeProvider.GetUtcNow();

        foreach (var chunk in logins.Chunk(100))
        {
            try
            {
                var queryParams = string.Join("&", chunk.Select(u => $"login={Uri.EscapeDataString(u.Trim())}"));
                var requestUri = $"https://api.twitch.tv/helix/users?{queryParams}";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Add("Client-Id", clientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError("Échec de la récupération des utilisateurs Twitch. Statut HTTP : {StatusCode}", response.StatusCode);
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _cachedToken = null;
                        _tokenExpiresAt = DateTimeOffset.MinValue;
                    }
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                var usersResponse = JsonSerializer.Deserialize<HelixUsersResponse>(content);
                if (usersResponse?.Data == null) continue;

                foreach (var u in usersResponse.Data)
                {
                    var userInfo = new TwitchUserInfoDto(u.Id, u.Login, u.DisplayName, u.ProfileImageUrl);
                    results.Add(userInfo);

                    _userIdCache[u.Login] = u.Id;
                    if (!string.IsNullOrWhiteSpace(u.ProfileImageUrl))
                    {
                        _avatarCache[u.Login] = (u.ProfileImageUrl, now);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la récupération des utilisateurs Twitch.");
            }
        }

        return results;
    }

    public async Task<string?> GetUserIdAsync(string userLogin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userLogin)) return null;

        var clean = userLogin.Trim();
        if (_userIdCache.TryGetValue(clean, out var cachedId))
        {
            return cachedId;
        }

        var users = await GetUsersByLoginAsync([clean], ct);
        var found = users.FirstOrDefault(u => string.Equals(u.Login, clean, StringComparison.OrdinalIgnoreCase));
        return found?.Id;
    }

    public async Task<string?> GetUserProfileImageUrlAsync(string userLogin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userLogin)) return null;

        var clean = userLogin.Trim();
        var now = timeProvider.GetUtcNow();
        if (_avatarCache.TryGetValue(clean, out var cached) && now - cached.CachedAt < TimeSpan.FromHours(1))
        {
            return cached.Url;
        }

        PruneAvatarCacheIfNeeded(now);

        var users = await GetUsersByLoginAsync([clean], ct);
        var user = users.FirstOrDefault(u => string.Equals(u.Login, clean, StringComparison.OrdinalIgnoreCase));
        return user?.ProfileImageUrl;
    }

    public async Task<TwitchSubscriptionResultDto> CreateEventSubSubscriptionAsync(
        string sessionId,
        string eventType,
        string broadcasterUserId,
        string? userAccessToken = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new TwitchSubscriptionResultDto(false, null, "L'identifiant de session WebSocket est requis.");
        }
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return new TwitchSubscriptionResultDto(false, null, "Le type d'événement EventSub est requis.");
        }
        if (string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            return new TwitchSubscriptionResultDto(false, null, "L'identifiant Twitch du diffuseur est requis.");
        }

        var clientId = GetClientId();
        var clientSecret = GetClientSecret();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return new TwitchSubscriptionResultDto(false, null, "Identifiants Twitch non configurés.");
        }

        var envUserToken = GetUserAccessToken();
        var token = !string.IsNullOrWhiteSpace(userAccessToken)
            ? userAccessToken.Trim()
            : (!string.IsNullOrWhiteSpace(envUserToken)
                ? envUserToken.Trim()
                : await GetValidAccessTokenAsync(clientId, clientSecret, ct));

        if (string.IsNullOrEmpty(token))
        {
            return new TwitchSubscriptionResultDto(false, null, "Impossible d'obtenir un token d'accès Twitch.");
        }

        try
        {
            var payload = new EventSubSubscriptionRequest(
                Type: eventType,
                Version: "1",
                Condition: new Dictionary<string, string> { ["broadcaster_user_id"] = broadcasterUserId },
                Transport: new EventSubTransportRequest("websocket", sessionId)
            );

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions");
            request.Headers.Add("Client-Id", clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Accepted || response.StatusCode == HttpStatusCode.OK)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var subResponse = JsonSerializer.Deserialize<HelixSubscriptionsResponse>(content);
                var subId = subResponse?.Data?.FirstOrDefault()?.Id;
                logger.LogInformation("Souscription EventSub '{EventType}' créée avec succès pour broadcaster {UserId} (Sub ID: {SubId}).", eventType, broadcasterUserId, subId);
                return new TwitchSubscriptionResultDto(true, subId, null);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                logger.LogInformation("Souscription EventSub '{EventType}' existe déjà pour broadcaster {UserId}.", eventType, broadcasterUserId);
                return new TwitchSubscriptionResultDto(true, null, "Souscription déjà active.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _cachedToken = null;
                _tokenExpiresAt = DateTimeOffset.MinValue;
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Échec de création de la souscription EventSub '{EventType}' ({StatusCode}) : {ErrorBody}", eventType, response.StatusCode, errorBody);
            return new TwitchSubscriptionResultDto(false, null, $"Erreur HTTP {response.StatusCode} : {errorBody}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception lors de la création de la souscription EventSub '{EventType}' pour broadcaster {UserId}.", eventType, broadcasterUserId);
            return new TwitchSubscriptionResultDto(false, null, ex.Message);
        }
    }

    public async Task<bool> DeleteEventSubSubscriptionAsync(
        string subscriptionId,
        string? userAccessToken = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId)) return false;

        var clientId = GetClientId();
        var clientSecret = GetClientSecret();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return false;
        }

        var envUserToken = GetUserAccessToken();
        var token = !string.IsNullOrWhiteSpace(userAccessToken)
            ? userAccessToken.Trim()
            : (!string.IsNullOrWhiteSpace(envUserToken)
                ? envUserToken.Trim()
                : await GetValidAccessTokenAsync(clientId, clientSecret, ct));

        if (string.IsNullOrEmpty(token)) return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.twitch.tv/helix/eventsub/subscriptions?id={Uri.EscapeDataString(subscriptionId)}");
            request.Headers.Add("Client-Id", clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Souscription EventSub {SubId} supprimée avec succès.", subscriptionId);
                return true;
            }

            logger.LogWarning("Échec de suppression de la souscription EventSub {SubId}. Statut : {StatusCode}", subscriptionId, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la suppression de la souscription EventSub {SubId}.", subscriptionId);
            return false;
        }
    }

    private void PruneAvatarCacheIfNeeded(DateTimeOffset now)
    {
        if (_avatarCache.Count <= 500) return;

        var expiredKeys = _avatarCache
            .Where(kv => now - kv.Value.CachedAt >= TimeSpan.FromHours(1))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _avatarCache.TryRemove(key, out _);
        }

        if (_avatarCache.Count > 1000)
        {
            _avatarCache.Clear();
        }
    }

    private async Task<string?> GetValidAccessTokenAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        if (!string.IsNullOrEmpty(_cachedToken) && now < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check locking
            now = timeProvider.GetUtcNow();
            if (!string.IsNullOrEmpty(_cachedToken) && now < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            var requestUri = "https://id.twitch.tv/oauth2/token";
            var formValues = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "client_credentials"
            };

            using var content = new FormUrlEncodedContent(formValues);
            using var response = await httpClient.PostAsync(requestUri, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Échec de l'obtention du token OAuth2 Twitch. Statut : {StatusCode}", response.StatusCode);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var tokenResponse = JsonSerializer.Deserialize<TwitchTokenResponseDto>(responseJson);
            if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                logger.LogError("Réponse de token Twitch invalide ou vide.");
                return null;
            }

            _cachedToken = tokenResponse.AccessToken;
            var safetyMarginSeconds = Math.Min(60, tokenResponse.ExpiresIn / 2);
            _tokenExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn - safetyMarginSeconds);

            logger.LogInformation("Nouveau token OAuth2 Twitch obtenu avec succès, expiration dans {ExpiresIn}s.", tokenResponse.ExpiresIn);
            return _cachedToken;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception levée lors de l'obtention du token OAuth2 Twitch.");
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed record HelixStreamsResponse(
        [property: JsonPropertyName("data")] List<HelixStreamItem>? Data
    );

    private sealed record HelixStreamItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("user_login")] string UserLogin,
        [property: JsonPropertyName("user_name")] string UserName,
        [property: JsonPropertyName("game_name")] string GameName,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("viewer_count")] int ViewerCount,
        [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("thumbnail_url")] string ThumbnailUrl
    );

    private sealed record HelixUsersResponse(
        [property: JsonPropertyName("data")] List<HelixUserItem>? Data
    );

    private sealed record HelixUserItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("profile_image_url")] string ProfileImageUrl
    );

    private sealed record EventSubSubscriptionRequest(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("condition")] Dictionary<string, string> Condition,
        [property: JsonPropertyName("transport")] EventSubTransportRequest Transport
    );

    private sealed record EventSubTransportRequest(
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("session_id")] string SessionId
    );

    private sealed record HelixSubscriptionsResponse(
        [property: JsonPropertyName("data")] List<HelixSubscriptionItem>? Data,
        [property: JsonPropertyName("total")] int? Total,
        [property: JsonPropertyName("total_cost")] int? TotalCost,
        [property: JsonPropertyName("max_total_cost")] int? MaxTotalCost
    );

    private sealed record HelixSubscriptionItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("cost")] int? Cost
    );

    private sealed record HelixClipsResponse(
        [property: JsonPropertyName("data")] List<HelixClipItem>? Data
    );

    private sealed record HelixClipItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("embed_url")] string? EmbedUrl,
        [property: JsonPropertyName("broadcaster_id")] string? BroadcasterId,
        [property: JsonPropertyName("broadcaster_name")] string? BroadcasterName,
        [property: JsonPropertyName("creator_id")] string? CreatorId,
        [property: JsonPropertyName("creator_name")] string? CreatorName,
        [property: JsonPropertyName("video_id")] string? VideoId,
        [property: JsonPropertyName("game_id")] string? GameId,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("view_count")] int ViewCount,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("thumbnail_url")] string? ThumbnailUrl,
        [property: JsonPropertyName("duration")] double Duration
    );
}
