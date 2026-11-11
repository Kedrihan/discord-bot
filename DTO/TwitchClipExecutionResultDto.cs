namespace DiscordBot.DTO;

public record TwitchClipExecutionResultDto(
    bool Success,
    string Message,
    int ProcessedClipsCount = 0
);
