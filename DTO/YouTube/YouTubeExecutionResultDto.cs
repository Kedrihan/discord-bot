namespace DiscordBot.DTO;

public record YouTubeExecutionResultDto(
    bool Success,
    string Message,
    int ProcessedVideosCount = 0
);
