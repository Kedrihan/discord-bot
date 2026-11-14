namespace DiscordBot.DTO.Permissions;

[Flags]
public enum GuildFeatureFlagsDto
{
    None = 0,
    Twitch = 1 << 0,
    YouTube = 1 << 1,
    All = Twitch | YouTube
}
