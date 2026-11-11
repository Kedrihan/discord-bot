namespace DiscordBot.DTO;

public record TwitchClipInfoDto(
    string Id,
    string Url,
    string EmbedUrl,
    string BroadcasterId,
    string BroadcasterName,
    string CreatorId,
    string CreatorName,
    string VideoId,
    string GameId,
    string Title,
    int ViewCount,
    DateTimeOffset CreatedAt,
    string ThumbnailUrl,
    double Duration
);
