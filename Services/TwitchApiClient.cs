using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TwitchApiClient(
    HttpClient httpClient,
    TwitchConfigService configService,
    TimeProvider timeProvider,
    ILogger<TwitchApiClient> logger)
{
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly ConcurrentDictionary<string, (string Url, DateTimeOffset CachedAt)> _avatarCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TwitchStreamInfo>> GetStreamsAsync(IEnumerable<string> userLogins, CancellationToken ct = default)
    {
        var logins = userLogins
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (logins.Count == 0) return [];

        var settings = configService.GetSettings();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            logger.LogWarning("Les identifiants de l'API Twitch (Client ID ou Client Secret) ne sont pas configurés.");
            return [];
        }

        var token = await GetValidAccessTokenAsync(settings.ClientId, settings.ClientSecret, ct);
        if (string.IsNullOrEmpty(token)) return [];

        var results = new List<TwitchStreamInfo>();

        // Helix limite à 100 requêtes de user_login par appel
        foreach (var chunk in logins.Chunk(100))
        {
            try
            {
                var queryParams = string.Join("&", chunk.Select(u => $"user_login={Uri.EscapeDataString(u.Trim())}"));
                var requestUri = $"https://api.twitch.tv/helix/streams?{queryParams}";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Add("Client-Id", settings.ClientId);
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
                    results.Add(new TwitchStreamInfo
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

    public async Task<string?> GetUserProfileImageUrlAsync(string userLogin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userLogin)) return null;

        var now = timeProvider.GetUtcNow();
        if (_avatarCache.TryGetValue(userLogin, out var cached) && now - cached.CachedAt < TimeSpan.FromHours(1))
        {
            return cached.Url;
        }

        // Nettoyage préventif du cache d'avatars si nécessaire
        if (_avatarCache.Count > 500)
        {
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

        var settings = configService.GetSettings();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            return null;
        }

        var token = await GetValidAccessTokenAsync(settings.ClientId, settings.ClientSecret, ct);
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            var requestUri = $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(userLogin.Trim())}";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add("Client-Id", settings.ClientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _cachedToken = null;
                    _tokenExpiresAt = DateTimeOffset.MinValue;
                }
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var usersResponse = JsonSerializer.Deserialize<HelixUsersResponse>(content);
            var user = usersResponse?.Data?.FirstOrDefault();
            if (user != null && !string.IsNullOrWhiteSpace(user.ProfileImageUrl))
            {
                _avatarCache[userLogin] = (user.ProfileImageUrl, now);
                return user.ProfileImageUrl;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la récupération de la photo de profil Twitch pour {UserLogin}", userLogin);
        }

        return null;
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
            var tokenResponse = JsonSerializer.Deserialize<TwitchTokenResponse>(responseJson);
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
}
