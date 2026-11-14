namespace DiscordBot.Core.Database.Entities;

public class GuildAdminPermissionEntity
{
    public int Id { get; set; }
    public string GuildId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int FeatureFlags { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
