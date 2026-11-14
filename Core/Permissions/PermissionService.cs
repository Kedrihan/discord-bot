using System.Security.Claims;
using Discord.WebSocket;
using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using DiscordBot.DTO.Permissions;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Core.Permissions;

public class PermissionService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    DiscordSocketClient? discordClient,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<PermissionService> logger)
{
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private List<GuildAdminPermissionEntity>? _cachedPermissions;
    private DateTimeOffset _lastCacheUpdate = DateTimeOffset.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public bool IsSuperAdmin(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return false;

        var clientId = configuration["DISCORD_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID");
        var clientSecret = configuration["DISCORD_CLIENT_SECRET"] ?? Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET");
        var isOAuthConfigured = !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);

        if (!isOAuthConfigured) return true;

        var adminUserId = configuration["DISCORD_ADMIN_USER_ID"] ?? Environment.GetEnvironmentVariable("DISCORD_ADMIN_USER_ID");
        if (string.IsNullOrWhiteSpace(adminUserId)) return false;

        var userId = GetUserId(user);
        return !string.IsNullOrWhiteSpace(userId) && string.Equals(userId, adminUserId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("id")?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    public bool HasAnyAdminAccess(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return false;
        if (IsSuperAdmin(user)) return true;

        var userId = GetUserId(user);
        if (string.IsNullOrWhiteSpace(userId)) return false;

        var cached = GetCachedPermissions();
        return cached.Any(p => p.UserId == userId && p.FeatureFlags != (int)GuildFeatureFlagsDto.None);
    }

    public IReadOnlyList<string> GetAuthorizedGuildIds(ClaimsPrincipal user, GuildFeatureFlagsDto? requiredFeature = null)
    {
        if (IsSuperAdmin(user))
        {
            var guildIds = new HashSet<string>();
            if (discordClient != null)
            {
                foreach (var g in discordClient.Guilds)
                {
                    guildIds.Add(g.Id.ToString());
                }
            }
            var cachedAll = GetCachedPermissions();
            foreach (var p in cachedAll)
            {
                if (!string.IsNullOrEmpty(p.GuildId)) guildIds.Add(p.GuildId);
            }
            return guildIds.ToList();
        }

        var userId = GetUserId(user);
        if (string.IsNullOrWhiteSpace(userId)) return [];

        var cached = GetCachedPermissions();
        return cached
            .Where(p => p.UserId == userId && (!requiredFeature.HasValue || ((GuildFeatureFlagsDto)p.FeatureFlags & requiredFeature.Value) == requiredFeature.Value))
            .Select(p => p.GuildId)
            .Distinct()
            .ToList();
    }

    public bool HasGuildFeatureAccess(ClaimsPrincipal user, string guildId, GuildFeatureFlagsDto feature)
    {
        if (string.IsNullOrWhiteSpace(guildId)) return false;
        if (IsSuperAdmin(user)) return true;

        var userId = GetUserId(user);
        if (string.IsNullOrWhiteSpace(userId)) return false;

        var cached = GetCachedPermissions();
        var perm = cached.FirstOrDefault(p => p.GuildId == guildId.Trim() && p.UserId == userId.Trim());
        if (perm == null) return false;

        return ((GuildFeatureFlagsDto)perm.FeatureFlags & feature) == feature;
    }

    public bool CanManageDiscordChannel(ClaimsPrincipal user, string discordChannelId, DiscordSocketClient client, GuildFeatureFlagsDto feature)
    {
        if (string.IsNullOrWhiteSpace(discordChannelId)) return false;
        if (IsSuperAdmin(user)) return true;

        if (!ulong.TryParse(discordChannelId.Trim(), out var channelId)) return false;
        if (client.GetChannel(channelId) is not SocketGuildChannel guildChannel) return false;

        return HasGuildFeatureAccess(user, guildChannel.Guild.Id.ToString(), feature);
    }

    public bool CanManageDiscordChannel(ClaimsPrincipal user, string discordChannelId, GuildFeatureFlagsDto feature)
    {
        if (string.IsNullOrWhiteSpace(discordChannelId)) return false;
        if (IsSuperAdmin(user)) return true;
        if (discordClient == null) return false;

        return CanManageDiscordChannel(user, discordChannelId, discordClient, feature);
    }

    public bool IsChannelInGuild(string discordChannelId, string guildId)
    {
        if (string.IsNullOrWhiteSpace(discordChannelId) || string.IsNullOrWhiteSpace(guildId)) return false;
        if (!ulong.TryParse(discordChannelId.Trim(), out var channelId) || channelId == 0) return false;
        if (!ulong.TryParse(guildId.Trim(), out var parsedGuildId) || parsedGuildId == 0) return false;
        if (discordClient == null) return true;
        if (discordClient.GetChannel(channelId) is not SocketGuildChannel guildChannel) return true;

        return string.Equals(guildChannel.Guild.Id.ToString(), guildId.Trim(), StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<GuildAdminPermissionDto>> GetAllPermissionsAsync(CancellationToken ct = default)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);
            var entities = await context.GuildAdminPermissions.AsNoTracking().ToListAsync(ct);
            _cachedPermissions = entities;
            _lastCacheUpdate = timeProvider.GetUtcNow();

            return entities.Select(e => new GuildAdminPermissionDto
            {
                Id = e.Id,
                GuildId = e.GuildId,
                UserId = e.UserId,
                FeatureFlags = (GuildFeatureFlagsDto)e.FeatureFlags,
                CreatedAt = e.CreatedAt
            }).ToList();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task UpsertPermissionAsync(string guildId, string userId, GuildFeatureFlagsDto featureFlags, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(guildId)) throw new ArgumentException("Guild ID cannot be empty.", nameof(guildId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User ID cannot be empty.", nameof(userId));

        var cleanGuildId = guildId.Trim();
        var cleanUserId = userId.Trim();

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var existing = await context.GuildAdminPermissions
            .FirstOrDefaultAsync(p => p.GuildId == cleanGuildId && p.UserId == cleanUserId, ct);

        if (existing != null)
        {
            existing.FeatureFlags = (int)featureFlags;
        }
        else
        {
            context.GuildAdminPermissions.Add(new GuildAdminPermissionEntity
            {
                GuildId = cleanGuildId,
                UserId = cleanUserId,
                FeatureFlags = (int)featureFlags,
                CreatedAt = timeProvider.GetUtcNow()
            });
        }

        await context.SaveChangesAsync(ct);
        await InvalidateCacheAsync(ct);
        logger.LogInformation("Permission mise à jour : Guild={GuildId}, User={UserId}, Flags={Flags}", cleanGuildId, cleanUserId, featureFlags);
    }

    public async Task RemovePermissionAsync(string guildId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(guildId) || string.IsNullOrWhiteSpace(userId)) return;

        var cleanGuildId = guildId.Trim();
        var cleanUserId = userId.Trim();

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var existing = await context.GuildAdminPermissions
            .FirstOrDefaultAsync(p => p.GuildId == cleanGuildId && p.UserId == cleanUserId, ct);

        if (existing != null)
        {
            context.GuildAdminPermissions.Remove(existing);
            await context.SaveChangesAsync(ct);
            await InvalidateCacheAsync(ct);
            logger.LogInformation("Permission supprimée : Guild={GuildId}, User={UserId}", cleanGuildId, cleanUserId);
        }
    }

    public async Task RemovePermissionAsync(int permissionId, CancellationToken ct = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var existing = await context.GuildAdminPermissions.FindAsync([permissionId], ct);
        if (existing != null)
        {
            context.GuildAdminPermissions.Remove(existing);
            await context.SaveChangesAsync(ct);
            await InvalidateCacheAsync(ct);
            logger.LogInformation("Permission ID={Id} supprimée.", permissionId);
        }
    }

    private List<GuildAdminPermissionEntity> GetCachedPermissions()
    {
        _cacheLock.Wait();
        try
        {
            var now = timeProvider.GetUtcNow();
            if (_cachedPermissions != null && now - _lastCacheUpdate < CacheDuration)
            {
                return _cachedPermissions;
            }

            using var context = dbContextFactory.CreateDbContext();
            _cachedPermissions = context.GuildAdminPermissions.AsNoTracking().ToList();
            _lastCacheUpdate = now;
            return _cachedPermissions;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task InvalidateCacheAsync(CancellationToken ct)
    {
        await _cacheLock.WaitAsync(ct);
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);
            _cachedPermissions = await context.GuildAdminPermissions.AsNoTracking().ToListAsync(ct);
            _lastCacheUpdate = timeProvider.GetUtcNow();
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
