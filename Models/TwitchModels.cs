using System.Text.Json.Serialization;

namespace DiscordBot.Models;

public class TrackedTwitchChannel
{
    public string TwitchUsername { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public string CustomMessage { get; set; } = "🔴 **{streamer}** est en direct sur Twitch !\n{url}";
    public bool IsEnabled { get; set; } = true;
}

public class TwitchSettings
{
    public bool IsEnabled { get; set; } = true;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string UserAccessToken { get; set; } = string.Empty;
    public int CheckIntervalSeconds { get; set; } = 30;
    public List<TrackedTwitchChannel> Channels { get; set; } = [];
}

public class TwitchStreamInfo
{
    public string StreamId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserLogin { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int ViewerCount { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ThumbnailUrl { get; set; } = string.Empty;
}

public class TwitchActiveStreamState
{
    public string TwitchUsername { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public ulong DiscordMessageId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}

public record TwitchExecutionResult(bool Success, string Message);

public record TwitchUserInfo(
    string Id,
    string Login,
    string DisplayName,
    string ProfileImageUrl
);

public record TwitchSubscriptionResult(
    bool Success,
    string? SubscriptionId,
    string? ErrorMessage
);

public enum TwitchEventSubStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}

public record TwitchTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("token_type")] string TokenType
);
