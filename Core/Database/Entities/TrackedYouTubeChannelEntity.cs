namespace DiscordBot.Core.Database.Entities;

public class TrackedYouTubeChannelEntity
{
    public int Id { get; set; }
    public int YouTubeSettingsId { get; set; }
    public string GuildId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ChannelHandle { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public string CustomMessage { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }

    public YouTubeSettingsEntity? YouTubeSettings { get; set; }
}
