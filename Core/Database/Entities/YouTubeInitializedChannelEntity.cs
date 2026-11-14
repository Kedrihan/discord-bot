namespace DiscordBot.Core.Database.Entities;

public class YouTubeInitializedChannelEntity
{
    public string ChannelId { get; set; } = string.Empty;
    public DateTimeOffset InitializedAt { get; set; }
}
