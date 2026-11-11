namespace DiscordBot.DTO;

public record TwitchSubscriptionResultDto(
    bool Success,
    string? SubscriptionId,
    string? ErrorMessage
);
