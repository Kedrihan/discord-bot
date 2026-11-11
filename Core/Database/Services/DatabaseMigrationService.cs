using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordBot.Core.Database.Entities;
using DiscordBot.DTO;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Core.Database.Services;

public class DatabaseMigrationService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILogger<DatabaseMigrationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InitializeAndMigrateAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Initialisation de la base de données SQLite...");

        var dataDir = fileSystem.Path.Combine(AppContext.BaseDirectory, "data");
        if (!fileSystem.Directory.Exists(dataDir))
        {
            fileSystem.Directory.CreateDirectory(dataDir);
        }
        var localDataDir = "data";
        if (!fileSystem.Directory.Exists(localDataDir))
        {
            fileSystem.Directory.CreateDirectory(localDataDir);
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken);

        await MigrateMorningBriefingSettingsAsync(context, cancellationToken);
        await MigrateTechWatchSettingsAsync(context, cancellationToken);
        await MigrateTwitchSettingsAsync(context, cancellationToken);
        await MigrateSentArticlesAsync(context, cancellationToken);
        await MigrateTwitchClipsAsync(context, cancellationToken);
        await MigrateTwitchLiveStateAsync(context, cancellationToken);

        logger.LogInformation("Initialisation et migration de la base de données SQLite terminées.");
    }

    private string? FindJsonFile(string filename)
    {
        var primaryPath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", filename);
        if (fileSystem.File.Exists(primaryPath)) return primaryPath;

        var relativePath = fileSystem.Path.Combine("data", filename);
        if (fileSystem.File.Exists(relativePath)) return relativePath;

        return null;
    }

    private void ArchiveMigratedFile(string filePath)
    {
        try
        {
            var destination = $"{filePath}.migrated";
            if (fileSystem.File.Exists(destination))
            {
                fileSystem.File.Delete(destination);
            }
            fileSystem.File.Move(filePath, destination);
            logger.LogInformation("Fichier JSON migré et archivé sous : {Destination}", destination);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible d'archiver le fichier {FilePath} après migration.", filePath);
        }
    }

    private async Task MigrateMorningBriefingSettingsAsync(BotDbContext context, CancellationToken ct)
    {
        var filePath = FindJsonFile("morning_briefing_settings.json");
        if (filePath != null)
        {
            try
            {
                var json = fileSystem.File.ReadAllText(filePath);
                var dto = JsonSerializer.Deserialize<MorningBriefingSettingsDto>(json, JsonOptions);
                if (dto != null)
                {
                    var existing = await context.MorningBriefingSettings.FindAsync([1], ct);
                    if (existing == null)
                    {
                        existing = new MorningBriefingSettingsEntity { Id = 1 };
                        context.MorningBriefingSettings.Add(existing);
                    }

                    existing.IsEnabled = dto.IsEnabled;
                    existing.ScheduleDays = dto.ScheduleDays;
                    existing.ScheduleTime = dto.ScheduleTime;
                    existing.DestinationType = dto.DestinationType;
                    existing.TargetId = dto.TargetId;
                    existing.Address = dto.Address;
                    existing.Latitude = dto.Latitude;
                    existing.Longitude = dto.Longitude;
                    existing.MorningStartHour = dto.MorningStartHour;
                    existing.MorningEndHour = dto.MorningEndHour;
                    existing.EveningStartHour = dto.EveningStartHour;
                    existing.EveningEndHour = dto.EveningEndHour;

                    await context.SaveChangesAsync(ct);
                    ArchiveMigratedFile(filePath);
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la migration de morning_briefing_settings.json.");
            }
        }

        if (!await context.MorningBriefingSettings.AnyAsync(ct))
        {
            context.MorningBriefingSettings.Add(new MorningBriefingSettingsEntity { Id = 1 });
            await context.SaveChangesAsync(ct);
        }
    }

    private async Task MigrateTechWatchSettingsAsync(BotDbContext context, CancellationToken ct)
    {
        var filePath = FindJsonFile("techwatch_settings.json");
        if (filePath != null)
        {
            try
            {
                var json = fileSystem.File.ReadAllText(filePath);
                var dto = JsonSerializer.Deserialize<TechWatchSettingsDto>(json, JsonOptions);
                if (dto != null)
                {
                    var existing = await context.TechWatchSettings.FindAsync([1], ct);
                    if (existing == null)
                    {
                        existing = new TechWatchSettingsEntity { Id = 1 };
                        context.TechWatchSettings.Add(existing);
                    }

                    existing.IsEnabled = dto.IsEnabled;
                    existing.ScheduleDays = dto.ScheduleDays;
                    existing.ScheduleTime = dto.ScheduleTime;
                    existing.DestinationType = dto.DestinationType;
                    existing.TargetId = dto.TargetId;
                    existing.Topics = dto.Topics;
                    existing.EnableDevTo = dto.EnableDevTo;
                    existing.EnableHackerNews = dto.EnableHackerNews;
                    existing.EnableDotnetBlogRss = dto.EnableDotnetBlogRss;
                    existing.CustomRssFeeds = dto.CustomRssFeeds;
                    existing.GeminiModel = dto.GeminiModel;
                    existing.MaxArticles = dto.MaxArticles;
                    existing.TranslateToFrench = dto.TranslateToFrench;

                    await context.SaveChangesAsync(ct);
                    ArchiveMigratedFile(filePath);
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la migration de techwatch_settings.json.");
            }
        }

        if (!await context.TechWatchSettings.AnyAsync(ct))
        {
            context.TechWatchSettings.Add(new TechWatchSettingsEntity { Id = 1 });
            await context.SaveChangesAsync(ct);
        }
    }

    private async Task MigrateTwitchSettingsAsync(BotDbContext context, CancellationToken ct)
    {
        var filePath = FindJsonFile("twitch_settings.json");
        if (filePath != null)
        {
            try
            {
                var json = fileSystem.File.ReadAllText(filePath);
                var dto = JsonSerializer.Deserialize<TwitchSettingsDto>(json, JsonOptions);
                if (dto != null)
                {
                    var existing = await context.TwitchSettings
                        .Include(s => s.Channels)
                        .FirstOrDefaultAsync(s => s.Id == 1, ct);

                    if (existing == null)
                    {
                        existing = new TwitchSettingsEntity { Id = 1 };
                        context.TwitchSettings.Add(existing);
                    }

                    existing.IsEnabled = dto.IsEnabled;
                    existing.IsClipsEnabled = dto.IsClipsEnabled;
                    existing.CheckIntervalSeconds = Math.Clamp(dto.CheckIntervalSeconds, 15, 3600);
                    existing.ClipCheckIntervalSeconds = Math.Clamp(dto.ClipCheckIntervalSeconds, 15, 3600);

                    context.TrackedTwitchChannels.RemoveRange(existing.Channels);
                    existing.Channels = dto.Channels.Select(c => new TrackedTwitchChannelEntity
                    {
                        TwitchSettingsId = 1,
                        TwitchUsername = c.TwitchUsername.Trim(),
                        DiscordChannelId = c.DiscordChannelId.Trim(),
                        CustomMessage = c.CustomMessage,
                        IsEnabled = c.IsEnabled,
                        IsClipEnabled = c.IsClipEnabled,
                        ClipDiscordChannelId = c.ClipDiscordChannelId.Trim(),
                        ClipCustomMessage = c.ClipCustomMessage
                    }).ToList();

                    await context.SaveChangesAsync(ct);
                    ArchiveMigratedFile(filePath);
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la migration de twitch_settings.json.");
            }
        }

        if (!await context.TwitchSettings.AnyAsync(ct))
        {
            context.TwitchSettings.Add(new TwitchSettingsEntity { Id = 1 });
            await context.SaveChangesAsync(ct);
        }
    }

    private async Task MigrateSentArticlesAsync(BotDbContext context, CancellationToken ct)
    {
        var filePath = FindJsonFile("sent_articles.json");
        if (filePath == null) return;

        try
        {
            var json = fileSystem.File.ReadAllText(filePath);
            var records = JsonSerializer.Deserialize<List<SentArticleRecordDto>>(json, JsonOptions);
            if (records != null && records.Count > 0)
            {
                foreach (var record in records)
                {
                    if (string.IsNullOrWhiteSpace(record.Url)) continue;
                    var normalized = record.Url.Trim().TrimEnd('/').ToLowerInvariant();
                    var existing = await context.SentArticles.FindAsync([normalized], ct);
                    if (existing == null)
                    {
                        context.SentArticles.Add(new SentArticleEntity
                        {
                            Url = normalized,
                            SentAt = record.SentAt
                        });
                    }
                }
                await context.SaveChangesAsync(ct);
            }
            ArchiveMigratedFile(filePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la migration de sent_articles.json.");
        }
    }

    private async Task MigrateTwitchClipsAsync(BotDbContext context, CancellationToken ct)
    {
        var filePath = FindJsonFile("twitch_clips_history.json");
        if (filePath == null) return;

        try
        {
            var json = fileSystem.File.ReadAllText(filePath);
            TwitchClipHistoryStoreDto? store = null;
            try
            {
                store = JsonSerializer.Deserialize<TwitchClipHistoryStoreDto>(json, JsonOptions);
            }
            catch
            {
                // Fallback to array of items
                var items = JsonSerializer.Deserialize<List<TwitchClipHistoryItemDto>>(json, JsonOptions);
                if (items != null)
                {
                    store = new TwitchClipHistoryStoreDto { Clips = items };
                }
            }

            if (store != null)
            {
                var now = timeProvider.GetUtcNow();
                foreach (var bId in store.InitializedBroadcasters ?? [])
                {
                    if (string.IsNullOrWhiteSpace(bId)) continue;
                    var trimmed = bId.Trim();
                    var existing = await context.TwitchInitializedBroadcasters.FindAsync([trimmed], ct);
                    if (existing == null)
                    {
                        context.TwitchInitializedBroadcasters.Add(new TwitchInitializedBroadcasterEntity
                        {
                            BroadcasterId = trimmed,
                            InitializedAt = now
                        });
                    }
                }

                foreach (var clip in store.Clips ?? [])
                {
                    if (string.IsNullOrWhiteSpace(clip.ClipId)) continue;
                    var trimmed = clip.ClipId.Trim();
                    var existing = await context.TwitchClips.FindAsync([trimmed], ct);
                    if (existing == null)
                    {
                        context.TwitchClips.Add(new TwitchClipHistoryEntity
                        {
                            ClipId = trimmed,
                            BroadcasterId = clip.BroadcasterId?.Trim() ?? string.Empty,
                            DiscoveredAt = clip.DiscoveredAt
                        });
                    }
                }
                await context.SaveChangesAsync(ct);
            }
            ArchiveMigratedFile(filePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la migration de twitch_clips_history.json.");
        }
    }

    private async Task MigrateTwitchLiveStateAsync(BotDbContext context, CancellationToken ct)
    {
        var filePath = FindJsonFile("twitch_live_state.json");
        if (filePath == null) return;

        try
        {
            var json = fileSystem.File.ReadAllText(filePath);
            List<TwitchActiveStreamStateDto>? states = null;
            try
            {
                states = JsonSerializer.Deserialize<List<TwitchActiveStreamStateDto>>(json, JsonOptions);
            }
            catch
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("activeStreams", out var streamsProp))
                    {
                        if (streamsProp.ValueKind == JsonValueKind.Object)
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, TwitchActiveStreamStateDto>>(streamsProp.GetRawText(), JsonOptions);
                            states = dict?.Values.ToList();
                        }
                        else if (streamsProp.ValueKind == JsonValueKind.Array)
                        {
                            states = JsonSerializer.Deserialize<List<TwitchActiveStreamStateDto>>(streamsProp.GetRawText(), JsonOptions);
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, TwitchActiveStreamStateDto>>(json, JsonOptions);
                        states = dict?.Values.ToList();
                    }
                }
                catch
                {
                    // Ignorer les formats incompatibles
                }
            }

            if (states != null && states.Count > 0)
            {
                foreach (var state in states)
                {
                    if (string.IsNullOrWhiteSpace(state.TwitchUsername)) continue;
                    var trimmed = state.TwitchUsername.Trim();
                    var existing = await context.TwitchActiveStreams.FindAsync([trimmed], ct);
                    if (existing == null)
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
                }
                await context.SaveChangesAsync(ct);
            }
            ArchiveMigratedFile(filePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la migration de twitch_live_state.json.");
        }
    }

    private sealed class SentArticleRecordDto
    {
        public string Url { get; set; } = string.Empty;
        public DateTimeOffset SentAt { get; set; }
    }

    private sealed class TwitchClipHistoryStoreDto
    {
        public List<string> InitializedBroadcasters { get; set; } = [];
        public List<TwitchClipHistoryItemDto> Clips { get; set; } = [];
    }
}
