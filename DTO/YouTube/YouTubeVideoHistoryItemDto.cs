namespace DiscordBot.DTO.YouTube;

public record YouTubeVideoHistoryItemDto(
    string VideoId,
    string ChannelId,
    DateTimeOffset DiscoveredAt
);
