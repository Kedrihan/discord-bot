namespace DiscordBot.DTO;

public class TwitchActiveStreamStateDto
{
    public string TwitchUsername { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public ulong DiscordMessageId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}
