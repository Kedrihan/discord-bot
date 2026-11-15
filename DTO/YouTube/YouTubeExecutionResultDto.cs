namespace DiscordBot.DTO.YouTube;

public record YouTubeExecutionResultDto(
    bool Success,
    string Message,
    int ProcessedVideosCount = 0
);
