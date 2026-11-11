namespace DiscordBot.Core.Database.Entities;

public class TwitchSettingsEntity
{
    public int Id { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public bool IsClipsEnabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 30;
    public int ClipCheckIntervalSeconds { get; set; } = 60;
    public List<TrackedTwitchChannelEntity> Channels { get; set; } = [];
}
