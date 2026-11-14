namespace DiscordBot.Core.Database.Entities;

public class YouTubeSettingsEntity
{
    public int Id { get; set; }
    public bool IsEnabled { get; set; }
    public int CheckIntervalMinutes { get; set; }
    public List<TrackedYouTubeChannelEntity> Channels { get; set; } = [];
}
