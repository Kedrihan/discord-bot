namespace DiscordBot.DTO;

public class TwitchSettingsDto
{
    public bool IsEnabled { get; set; } = true;
    public bool IsClipsEnabled { get; set; } = true;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string UserAccessToken { get; set; } = string.Empty;
    public int CheckIntervalSeconds { get; set; } = 30;
    public int ClipCheckIntervalSeconds { get; set; } = 60;
    public List<TrackedTwitchChannelDto> Channels { get; set; } = [];
}
