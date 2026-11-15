namespace DiscordBot.DTO.YouTube;

public record YouTubeVideoInfoDto(
    string VideoId,
    string ChannelId,
    string ChannelTitle,
    string Title,
    string Url,
    string ThumbnailUrl,
    DateTimeOffset PublishedAt,
    string VideoType
);
