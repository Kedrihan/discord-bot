namespace DiscordBot.DTO.Permissions;

public record GuildAdminPermissionDto
{
    public int Id { get; init; }
    public string GuildId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public GuildFeatureFlagsDto FeatureFlags { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
