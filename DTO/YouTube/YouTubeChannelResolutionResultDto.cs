namespace DiscordBot.DTO.YouTube;

public record YouTubeChannelResolutionResultDto(
    bool Success,
    string? ChannelId,
    string? ChannelTitle,
    string? ErrorMessage
);
