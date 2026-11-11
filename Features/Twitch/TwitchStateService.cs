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

    public TwitchActiveStreamStateDto? GetActiveStream(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;

        try
        {
            var trimmed = username.Trim();
            using var context = dbContextFactory.CreateDbContext();
            var entity = context.TwitchActiveStreams.FirstOrDefault(s => s.TwitchUsername.ToLower() == trimmed.ToLower());
            if (entity == null) return null;

            return new TwitchActiveStreamStateDto
            {
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
            var trimmed = state.TwitchUsername.Trim();
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            var existing = await context.TwitchActiveStreams.FirstOrDefaultAsync(s => s.TwitchUsername.ToLower() == trimmed.ToLower(), ct);
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
                    TwitchUsername = trimmed,
                    StreamId = state.StreamId,
                    DiscordChannelId = state.DiscordChannelId,
                    DiscordMessageId = state.DiscordMessageId,
                    StartedAt = state.StartedAt
                });
            }

            await context.SaveChangesAsync(ct);
            logger.LogInformation("État du stream actif pour {Username} enregistré en base SQLite (Message ID: {MessageId}).", state.TwitchUsername, state.DiscordMessageId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'enregistrement de l'état du stream actif pour {Username}.", state.TwitchUsername);
        }
    }

    public async Task RemoveActiveStreamAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return;

        try
        {
            var trimmed = username.Trim();
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            var existing = await context.TwitchActiveStreams.FirstOrDefaultAsync(s => s.TwitchUsername.ToLower() == trimmed.ToLower(), ct);
            if (existing != null)
            {
                context.TwitchActiveStreams.Remove(existing);
                await context.SaveChangesAsync(ct);
                logger.LogInformation("Stream actif retiré de SQLite pour le streamer {Username}.", username);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la suppression de l'état du stream actif pour {Username}.", username);
        }
    }
}
