namespace DiscordBot.DTO.Twitch;

public record TwitchClipHistoryItemDto(
    string ClipId,
    string BroadcasterId,
    DateTimeOffset DiscoveredAt
);
