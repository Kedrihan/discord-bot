namespace DiscordBot.DTO;

public class TwitchSettingsDto
{
    public bool IsEnabled { get; set; } = true;
    public bool IsClipsEnabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 30;
    public int ClipCheckIntervalSeconds { get; set; } = 60;
    public List<TrackedTwitchChannelDto> Channels { get; set; } = [];
}
