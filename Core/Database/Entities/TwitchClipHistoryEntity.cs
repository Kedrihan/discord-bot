namespace DiscordBot.Core.Database.Entities;

public class TwitchClipHistoryEntity
{
    public string GuildId { get; set; } = string.Empty;
    public string ClipId { get; set; } = string.Empty;
    public string BroadcasterId { get; set; } = string.Empty;
    public DateTimeOffset DiscoveredAt { get; set; }
}
