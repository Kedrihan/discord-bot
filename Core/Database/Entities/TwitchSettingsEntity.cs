namespace DiscordBot.Core.Database.Entities;

public class TwitchSettingsEntity
{
    public int Id { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsClipsEnabled { get; set; }
    public int CheckIntervalSeconds { get; set; }
    public int ClipCheckIntervalSeconds { get; set; }
    public List<TrackedTwitchChannelEntity> Channels { get; set; } = [];
}
