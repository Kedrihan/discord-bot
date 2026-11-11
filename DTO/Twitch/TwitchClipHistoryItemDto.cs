namespace DiscordBot.DTO;

public record TwitchClipHistoryItemDto(
    string ClipId,
    string BroadcasterId,
    DateTimeOffset DiscoveredAt
);
