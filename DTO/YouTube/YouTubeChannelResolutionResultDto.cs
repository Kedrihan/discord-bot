namespace DiscordBot.DTO;

public record YouTubeChannelResolutionResultDto(
    bool Success,
    string? ChannelId,
    string? ChannelTitle,
    string? ErrorMessage
);
