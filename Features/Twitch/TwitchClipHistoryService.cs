using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Services;

public class TwitchClipHistoryService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    TimeProvider timeProvider,
    ILogger<TwitchClipHistoryService> logger)
{
    private const int MaxClipsRetained = 5000;

    public bool IsClipSeen(string clipId)
    {
        if (string.IsNullOrWhiteSpace(clipId)) return false;

        try
        {
            var trimmed = clipId.Trim();
            using var context = dbContextFactory.CreateDbContext();
            return context.TwitchClips.Any(c => c.ClipId == trimmed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la vérification du clip : {ClipId}", clipId);
            return false;
        }
    }

    public bool IsBroadcasterInitialized(string broadcasterId)
    {
        if (string.IsNullOrWhiteSpace(broadcasterId)) return false;

        try
        {
            var trimmed = broadcasterId.Trim();
            using var context = dbContextFactory.CreateDbContext();
            return context.TwitchInitializedBroadcasters.Any(b => b.BroadcasterId == trimmed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la vérification de l'initialisation du broadcaster : {BroadcasterId}", broadcasterId);
            return false;
        }
    }

    public async Task InitializeBroadcasterAsync(string broadcasterId, IEnumerable<string> existingClipIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(broadcasterId)) return;

        var trimmedBroadcasterId = broadcasterId.Trim();
        var now = timeProvider.GetUtcNow();

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            var existingBroadcaster = await context.TwitchInitializedBroadcasters.FindAsync([trimmedBroadcasterId], ct);
            if (existingBroadcaster == null)
            {
                context.TwitchInitializedBroadcasters.Add(new TwitchInitializedBroadcasterEntity
                {
                    BroadcasterId = trimmedBroadcasterId,
                    InitializedAt = now
                });
            }

            foreach (var clipId in existingClipIds)
            {
                if (string.IsNullOrWhiteSpace(clipId)) continue;
                var trimmed = clipId.Trim();
                var existingClip = await context.TwitchClips.FindAsync([trimmed], ct);
                if (existingClip == null)
                {
                    context.TwitchClips.Add(new TwitchClipHistoryEntity
                    {
                        ClipId = trimmed,
                        BroadcasterId = trimmedBroadcasterId,
                        DiscoveredAt = now
                    });
                }
            }

            await PruneOldClipsAndSaveAsync(context, now, ct);
            logger.LogInformation("Broadcaster Twitch {BroadcasterId} initialisé avec protection contre le cold-start.", broadcasterId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'initialisation du broadcaster {BroadcasterId}.", broadcasterId);
        }
    }

    public async Task MarkClipsAsSeenAsync(IEnumerable<string> clipIds, string broadcasterId, CancellationToken ct = default)
    {
        var clipIdList = clipIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();
        if (clipIdList.Count == 0 && string.IsNullOrWhiteSpace(broadcasterId)) return;

        var trimmedBroadcasterId = broadcasterId?.Trim() ?? string.Empty;
        var now = timeProvider.GetUtcNow();

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            if (!string.IsNullOrEmpty(trimmedBroadcasterId))
            {
                var existingBroadcaster = await context.TwitchInitializedBroadcasters.FindAsync([trimmedBroadcasterId], ct);
                if (existingBroadcaster == null)
                {
                    context.TwitchInitializedBroadcasters.Add(new TwitchInitializedBroadcasterEntity
                    {
                        BroadcasterId = trimmedBroadcasterId,
                        InitializedAt = now
                    });
                }
            }

            foreach (var clipId in clipIdList)
            {
                var existingClip = await context.TwitchClips.FindAsync([clipId], ct);
                if (existingClip == null)
                {
                    context.TwitchClips.Add(new TwitchClipHistoryEntity
                    {
                        ClipId = clipId,
                        BroadcasterId = trimmedBroadcasterId,
                        DiscoveredAt = now
                    });
                }
            }

            await PruneOldClipsAndSaveAsync(context, now, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du marquage des clips comme vus pour le broadcaster {BroadcasterId}.", broadcasterId);
        }
    }

    private static async Task PruneOldClipsAndSaveAsync(BotDbContext context, DateTimeOffset now, CancellationToken ct)
    {
        // Nettoyage des clips de plus de 30 jours
        var cutoff = now.AddDays(-30);
        var oldClips = context.TwitchClips.Where(c => c.DiscoveredAt < cutoff);
        context.TwitchClips.RemoveRange(oldClips);

        await context.SaveChangesAsync(ct);

        // Plafonnement à MaxClipsRetained (5000)
        var totalCount = await context.TwitchClips.CountAsync(ct);
        if (totalCount > MaxClipsRetained)
        {
            var excess = totalCount - MaxClipsRetained;
            var clipsToRemove = await context.TwitchClips
                .OrderBy(c => c.DiscoveredAt)
                .Take(excess)
                .ToListAsync(ct);

            context.TwitchClips.RemoveRange(clipsToRemove);
            await context.SaveChangesAsync(ct);
        }
    }
}
