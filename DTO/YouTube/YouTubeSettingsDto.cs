namespace DiscordBot.DTO.YouTube;

public class YouTubeSettingsDto
{
    public bool IsEnabled { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 15;
    public List<TrackedYouTubeChannelDto> Channels { get; set; } = [];
}
