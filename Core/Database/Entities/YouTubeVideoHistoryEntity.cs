namespace DiscordBot.Core.Database.Entities;

public class YouTubeVideoHistoryEntity
{
    public string GuildId { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public DateTimeOffset DiscoveredAt { get; set; }
}
