namespace DiscordBot.DTO;

public class TrackedYouTubeChannelDto
{
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ChannelHandle { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public string CustomMessage { get; set; } = "🎥 Nouvelle vidéo de **{channel}** !\n{url}";
    public bool IsEnabled { get; set; } = true;
}
