namespace DiscordBot.DTO;

public record TwitchUserInfoDto(
    string Id,
    string Login,
    string DisplayName,
    string ProfileImageUrl
);
