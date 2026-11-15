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

    public bool IsVideoSeen(string guildId, string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return false;

        try
        {
            var trimmedVideo = videoId.Trim();
            var trimmedGuild = guildId?.Trim() ?? string.Empty;
            using var context = dbContextFactory.CreateDbContext();
            return context.YouTubeVideoHistory.Any(v => v.GuildId == trimmedGuild && v.VideoId == trimmedVideo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la vérification de la vidéo YouTube Guild={GuildId}, {VideoId}", guildId, videoId);
            return false;
        }
    }

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

    public bool IsChannelInitialized(string guildId, string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return false;

        try
        {
            var trimmedChannel = channelId.Trim();
            var trimmedGuild = guildId?.Trim() ?? string.Empty;
            using var context = dbContextFactory.CreateDbContext();
            return context.YouTubeInitializedChannels.Any(c => c.GuildId == trimmedGuild && c.ChannelId == trimmedChannel);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la vérification de l'initialisation de la chaîne YouTube Guild={GuildId}, {ChannelId}", guildId, channelId);
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

    public async Task InitializeChannelAsync(string guildId, string channelId, IEnumerable<string> existingVideoIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return;

        var trimmedChannelId = channelId.Trim();
        var trimmedGuildId = guildId?.Trim() ?? string.Empty;
        var now = timeProvider.GetUtcNow();

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            var existingChannel = await context.YouTubeInitializedChannels.FindAsync([trimmedGuildId, trimmedChannelId], ct);
            if (existingChannel == null)
            {
                context.YouTubeInitializedChannels.Add(new YouTubeInitializedChannelEntity
                {
                    GuildId = trimmedGuildId,
                    ChannelId = trimmedChannelId,
                    InitializedAt = now
                });
            }

            foreach (var videoId in existingVideoIds)
            {
                if (string.IsNullOrWhiteSpace(videoId)) continue;
                var trimmed = videoId.Trim();
                var existingVideo = await context.YouTubeVideoHistory.FindAsync([trimmedGuildId, trimmed], ct);
                if (existingVideo == null)
                {
                    context.YouTubeVideoHistory.Add(new YouTubeVideoHistoryEntity
                    {
                        GuildId = trimmedGuildId,
                        VideoId = trimmed,
                        ChannelId = trimmedChannelId,
                        DiscoveredAt = now
                    });
                }
            }

            await PruneOldVideosAndSaveAsync(context, now, ct);
            logger.LogInformation("Chaîne YouTube Guild={GuildId}, {ChannelId} initialisée avec protection contre le cold-start.", trimmedGuildId, channelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'initialisation de la chaîne YouTube Guild={GuildId}, {ChannelId}.", trimmedGuildId, channelId);
        }
    }

    public Task InitializeChannelAsync(string channelId, IEnumerable<string> existingVideoIds, CancellationToken ct = default)
        => InitializeChannelAsync(string.Empty, channelId, existingVideoIds, ct);

    public async Task MarkVideosAsSeenAsync(string guildId, IEnumerable<string> videoIds, string channelId, CancellationToken ct = default)
    {
        var videoIdList = videoIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();
        if (videoIdList.Count == 0 && string.IsNullOrWhiteSpace(channelId)) return;

        var trimmedChannelId = channelId?.Trim() ?? string.Empty;
        var trimmedGuildId = guildId?.Trim() ?? string.Empty;
        var now = timeProvider.GetUtcNow();

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);

            if (!string.IsNullOrEmpty(trimmedChannelId))
            {
                var existingChannel = await context.YouTubeInitializedChannels.FindAsync([trimmedGuildId, trimmedChannelId], ct);
                if (existingChannel == null)
                {
                    context.YouTubeInitializedChannels.Add(new YouTubeInitializedChannelEntity
                    {
                        GuildId = trimmedGuildId,
                        ChannelId = trimmedChannelId,
                        InitializedAt = now
                    });
                }
            }

            foreach (var videoId in videoIdList)
            {
                var existingVideo = await context.YouTubeVideoHistory.FindAsync([trimmedGuildId, videoId], ct);
                if (existingVideo == null)
                {
                    context.YouTubeVideoHistory.Add(new YouTubeVideoHistoryEntity
                    {
                        GuildId = trimmedGuildId,
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
            logger.LogError(ex, "Erreur lors du marquage des vidéos comme vues pour Guild={GuildId}, Chaîne={ChannelId}.", trimmedGuildId, channelId);
        }
    }

    public Task MarkVideosAsSeenAsync(IEnumerable<string> videoIds, string channelId, CancellationToken ct = default)
        => MarkVideosAsSeenAsync(string.Empty, videoIds, channelId, ct);

    private static async Task PruneOldVideosAndSaveAsync(BotDbContext context, DateTimeOffset now, CancellationToken ct)
    {
        // Nettoyage des vidéos de plus de 30 jours
        var cutoff = now.AddDays(-30);
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
