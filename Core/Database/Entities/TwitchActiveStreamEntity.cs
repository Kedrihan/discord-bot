namespace DiscordBot.Core.Database.Entities;

public class TwitchActiveStreamEntity
{
    public string GuildId { get; set; } = string.Empty;
    public string TwitchUsername { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public ulong DiscordMessageId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}
