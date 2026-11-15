namespace DiscordBot.DTO.Twitch;

public record TwitchUserInfoDto(
    string Id,
    string Login,
    string DisplayName,
    string ProfileImageUrl
);
