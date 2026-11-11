namespace DiscordBot.Core.Database.Entities;

public class TwitchInitializedBroadcasterEntity
{
    public string BroadcasterId { get; set; } = string.Empty;
    public DateTimeOffset InitializedAt { get; set; }
}
