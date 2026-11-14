namespace DiscordBot.Core.Database.Entities;

public class TrackedTwitchChannelEntity
{
    public int Id { get; set; }
    public int TwitchSettingsId { get; set; }
    public string GuildId { get; set; } = string.Empty;
    public string TwitchUsername { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public string CustomMessage { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsClipEnabled { get; set; }
    public string ClipDiscordChannelId { get; set; } = string.Empty;
    public string ClipCustomMessage { get; set; } = string.Empty;

    public TwitchSettingsEntity? TwitchSettings { get; set; }
}
