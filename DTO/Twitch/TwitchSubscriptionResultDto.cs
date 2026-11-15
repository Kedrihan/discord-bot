namespace DiscordBot.DTO.Twitch;

public record TwitchSubscriptionResultDto(
    bool Success,
    string? SubscriptionId,
    string? ErrorMessage
);
