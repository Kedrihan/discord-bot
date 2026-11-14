using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using DiscordBot.DTO;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Services;

public class TwitchStateService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger<TwitchStateService> logger)
{
    public IReadOnlyList<TwitchActiveStreamStateDto> GetActiveStreams()
    {
        try
        {
            using var context = dbContextFactory.CreateDbContext();
            return context.TwitchActiveStreams
                .Select(s => new TwitchActiveStreamStateDto
                {
                    GuildId = s.GuildId,
                    TwitchUsername = s.TwitchUsername,
                    StreamId = s.StreamId,
                    DiscordChannelId = s.DiscordChannelId,
                    DiscordMessageId = s.DiscordMessageId,
                    StartedAt = s.StartedAt
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la récupération des streams actifs.");
            return [];
        }
    }

    public IReadOnlyList<TwitchActiveStreamStateDto> GetActiveStreams(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return [];

        try
        {
            var trimmed = username.Trim().ToLower();
            using var context = dbContextFactory.CreateDbContext();
            return context.TwitchActiveStreams
                .Where(s => s.TwitchUsername.ToLower() == trimmed)
                .Select(s => new TwitchActiveStreamStateDto
                {
                    GuildId = s.GuildId,
                    TwitchUsername = s.TwitchUsername,
                    StreamId = s.StreamId,
                    DiscordChannelId = s.DiscordChannelId,
                    DiscordMessageId = s.DiscordMessageId,
                    StartedAt = s.StartedAt
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la récupération des streams actifs pour {Username}.", username);
            return [];
        }
    }

    public TwitchActiveStreamStateDto? GetActiveStream(string guildId, string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;

        try
        {
            var trimmedUser = username.Trim().ToLower();
            var trimmedGuild = guildId?.Trim() ?? string.Empty;
            using var context = dbContextFactory.CreateDbContext();
            var entity = context.TwitchActiveStreams
                .FirstOrDefault(s => s.GuildId == trimmedGuild && s.TwitchUsername.ToLower() == trimmedUser);

            if (entity == null) return null;

            return new TwitchActiveStreamStateDto
            {
                GuildId = entity.GuildId,
                TwitchUsername = entity.TwitchUsername,
                StreamId = entity.StreamId,
                DiscordChannelId = entity.DiscordChannelId,
                DiscordMessageId = entity.DiscordMessageId,
                StartedAt = entity.StartedAt
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la récupération du stream actif pour Guild={GuildId}, {Username}.", guildId, username);
            return null;
        }
    }

    public TwitchActiveStreamStateDto? GetActiveStream(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;

        try
        {
            var trimmed = username.Trim().ToLower();
            using var context = dbContextFactory.CreateDbContext();
            var entity = context.TwitchActiveStreams.FirstOrDefault(s => s.TwitchUsername.ToLower() == trimmed);
            if (entity == null) return null;

            return new TwitchActiveStreamStateDto
            {
                GuildId = entity.GuildId,
                TwitchUsername = entity.TwitchUsername,
                StreamId = entity.StreamId,
                DiscordChannelId = entity.DiscordChannelId,
                DiscordMessageId = entity.DiscordMessageId,
                StartedAt = entity.StartedAt
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la récupération du stream actif pour {Username}.", username);
            return null;
        }
    }

    public async Task SetActiveStreamAsync(TwitchActiveStreamStateDto state, CancellationToken ct = default)
    {
        try
        {
            var trimmedUser = state.TwitchUsername.Trim();
            var trimmedGuild = state.GuildId?.Trim() ?? string.Empty;
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            var existing = await context.TwitchActiveStreams
                .FirstOrDefaultAsync(s => s.GuildId == trimmedGuild && s.TwitchUsername.ToLower() == trimmedUser.ToLower(), ct);

            if (existing != null)
            {
                existing.StreamId = state.StreamId;
                existing.DiscordChannelId = state.DiscordChannelId;
                existing.DiscordMessageId = state.DiscordMessageId;
                existing.StartedAt = state.StartedAt;
            }
            else
            {
                context.TwitchActiveStreams.Add(new TwitchActiveStreamEntity
                {
                    GuildId = trimmedGuild,
                    TwitchUsername = trimmedUser,
                    StreamId = state.StreamId,
                    DiscordChannelId = state.DiscordChannelId,
                    DiscordMessageId = state.DiscordMessageId,
                    StartedAt = state.StartedAt
                });
            }

            await context.SaveChangesAsync(ct);
            logger.LogInformation("État du stream actif pour Guild={GuildId}, {Username} enregistré en base SQLite (Message ID: {MessageId}).",
                trimmedGuild, state.TwitchUsername, state.DiscordMessageId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'enregistrement de l'état du stream actif pour Guild={GuildId}, {Username}.", state.GuildId, state.TwitchUsername);
        }
    }

    public async Task RemoveActiveStreamAsync(string guildId, string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return;

        try
        {
            var trimmedUser = username.Trim().ToLower();
            var trimmedGuild = guildId?.Trim() ?? string.Empty;
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            var existing = await context.TwitchActiveStreams
                .FirstOrDefaultAsync(s => s.GuildId == trimmedGuild && s.TwitchUsername.ToLower() == trimmedUser, ct);

            if (existing != null)
            {
                context.TwitchActiveStreams.Remove(existing);
                await context.SaveChangesAsync(ct);
                logger.LogInformation("Stream actif retiré de SQLite pour Guild={GuildId}, {Username}.", trimmedGuild, username);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la suppression de l'état du stream actif pour Guild={GuildId}, {Username}.", guildId, username);
        }
    }

    public async Task RemoveActiveStreamAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return;

        try
        {
            var trimmedUser = username.Trim().ToLower();
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            var matching = await context.TwitchActiveStreams
                .Where(s => s.TwitchUsername.ToLower() == trimmedUser)
                .ToListAsync(ct);

            if (matching.Count > 0)
            {
                context.TwitchActiveStreams.RemoveRange(matching);
                await context.SaveChangesAsync(ct);
                logger.LogInformation("Streams actifs ({Count}) retirés de SQLite pour {Username}.", matching.Count, username);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la suppression de l'état du stream actif pour {Username}.", username);
        }
    }
}
