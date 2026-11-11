namespace DiscordBot.DTO;

public class TrackedTwitchChannelDto
{
    public string TwitchUsername { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public string CustomMessage { get; set; } = "🔴 **{streamer}** est en direct sur Twitch !\n{url}";
    public bool IsEnabled { get; set; } = true;
    public bool IsClipEnabled { get; set; } = false;
    public string ClipDiscordChannelId { get; set; } = string.Empty;
    public string ClipCustomMessage { get; set; } = "🎬 Nouveau clip de **{streamer}** créé par **{creator}** !\n{url}";
}
