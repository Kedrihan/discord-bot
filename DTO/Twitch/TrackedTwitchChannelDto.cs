namespace DiscordBot.DTO.Twitch;

public class TrackedTwitchChannelDto
{
    public string GuildId { get; set; } = string.Empty;
    public string TwitchUsername { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public string CustomMessage { get; set; } = "ðŸ”´ **{streamer}** est en direct sur Twitch !\n{url}";
    public bool IsEnabled { get; set; } = true;
    public bool IsClipEnabled { get; set; } = false;
    public string ClipDiscordChannelId { get; set; } = string.Empty;
    public string ClipCustomMessage { get; set; } = "ðŸŽ¬ Nouveau clip de **{streamer}** crÃ©Ã© par **{creator}** !\n{url}";
}
