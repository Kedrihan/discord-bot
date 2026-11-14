namespace DiscordBot.DTO;

public record YouTubeVideoHistoryItemDto(
    string VideoId,
    string ChannelId,
    DateTimeOffset DiscoveredAt
);
