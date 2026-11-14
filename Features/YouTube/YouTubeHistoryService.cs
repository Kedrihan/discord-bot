using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Features.YouTube;

public class YouTubeHistoryService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    TimeProvider timeProvider,
    ILogger<YouTubeHistoryService> logger)
{
    private const int MaxVideosRetained = 5000;

    public bool IsVideoSeen(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return false;

        try
        {
            var trimmed = videoId.Trim();
            using var context = dbContextFactory.CreateDbContext();
            return context.YouTubeVideoHistory.Any(v => v.VideoId == trimmed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la vérification de la vidéo YouTube : {VideoId}", videoId);
            return false;
        }
    }

    public bool IsChannelInitialized(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return false;

        try
        {
            var trimmed = channelId.Trim();
            using var context = dbContextFactory.CreateDbContext();
            return context.YouTubeInitializedChannels.Any(c => c.ChannelId == trimmed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la vérification de l'initialisation de la chaîne YouTube : {ChannelId}", channelId);
            return false;
        }
    }

    public async Task InitializeChannelAsync(string channelId, IEnumerable<string> existingVideoIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return;

        var trimmedChannelId = channelId.Trim();
        var now = timeProvider.GetUtcNow();

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            var existingChannel = await context.YouTubeInitializedChannels.FindAsync([trimmedChannelId], ct);
            if (existingChannel == null)
            {
                context.YouTubeInitializedChannels.Add(new YouTubeInitializedChannelEntity
                {
                    ChannelId = trimmedChannelId,
                    InitializedAt = now
                });
            }

            foreach (var videoId in existingVideoIds)
            {
                if (string.IsNullOrWhiteSpace(videoId)) continue;
                var trimmed = videoId.Trim();
                var existingVideo = await context.YouTubeVideoHistory.FindAsync([trimmed], ct);
                if (existingVideo == null)
                {
                    context.YouTubeVideoHistory.Add(new YouTubeVideoHistoryEntity
                    {
                        VideoId = trimmed,
                        ChannelId = trimmedChannelId,
                        DiscoveredAt = now
                    });
                }
            }

            await PruneOldVideosAndSaveAsync(context, now, ct);
            logger.LogInformation("Chaîne YouTube {ChannelId} initialisée avec protection contre le cold-start.", channelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'initialisation de la chaîne YouTube {ChannelId}.", channelId);
        }
    }

    public async Task MarkVideosAsSeenAsync(IEnumerable<string> videoIds, string channelId, CancellationToken ct = default)
    {
        var videoIdList = videoIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();
        if (videoIdList.Count == 0 && string.IsNullOrWhiteSpace(channelId)) return;

        var trimmedChannelId = channelId?.Trim() ?? string.Empty;
        var now = timeProvider.GetUtcNow();

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            if (!string.IsNullOrEmpty(trimmedChannelId))
            {
                var existingChannel = await context.YouTubeInitializedChannels.FindAsync([trimmedChannelId], ct);
                if (existingChannel == null)
                {
                    context.YouTubeInitializedChannels.Add(new YouTubeInitializedChannelEntity
                    {
                        ChannelId = trimmedChannelId,
                        InitializedAt = now
                    });
                }
            }

            foreach (var videoId in videoIdList)
            {
                var existingVideo = await context.YouTubeVideoHistory.FindAsync([videoId], ct);
                if (existingVideo == null)
                {
                    context.YouTubeVideoHistory.Add(new YouTubeVideoHistoryEntity
                    {
                        VideoId = videoId,
                        ChannelId = trimmedChannelId,
                        DiscoveredAt = now
                    });
                }
            }

            await PruneOldVideosAndSaveAsync(context, now, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du marquage des vidéos comme vues pour la chaîne YouTube {ChannelId}.", channelId);
        }
    }

    private static async Task PruneOldVideosAndSaveAsync(BotDbContext context, DateTimeOffset now, CancellationToken ct)
    {
        // Nettoyage des vidéos de plus de 90 jours
        var cutoff = now.AddDays(-90);
        var oldVideos = context.YouTubeVideoHistory.Where(v => v.DiscoveredAt < cutoff);
        context.YouTubeVideoHistory.RemoveRange(oldVideos);

        await context.SaveChangesAsync(ct);

        // Plafonnement à MaxVideosRetained (5000)
        var totalCount = await context.YouTubeVideoHistory.CountAsync(ct);
        if (totalCount > MaxVideosRetained)
        {
            var excess = totalCount - MaxVideosRetained;
            var videosToRemove = await context.YouTubeVideoHistory
                .OrderBy(v => v.DiscoveredAt)
                .Take(excess)
                .ToListAsync(ct);

            context.YouTubeVideoHistory.RemoveRange(videosToRemove);
            await context.SaveChangesAsync(ct);
        }
    }
}
