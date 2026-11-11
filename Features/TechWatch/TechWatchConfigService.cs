using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using DiscordBot.DTO;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Services;

public class TechWatchConfigService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger<TechWatchConfigService> logger)
{
    private readonly object _lock = new();
    private TechWatchSettingsDto? _settings;

    public TechWatchSettingsDto GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    public async Task<TechWatchSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TechWatchSettings.FindAsync([1], cancellationToken);
        if (entity != null)
        {
            var dto = MapEntityToDto(entity);
            lock (_lock)
            {
                _settings = dto;
            }
            return dto;
        }

        return new TechWatchSettingsDto();
    }

    public async Task SaveSettingsAsync(TechWatchSettingsDto newSettings, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TechWatchSettings.FindAsync([1], cancellationToken);
        if (entity == null)
        {
            entity = new TechWatchSettingsEntity { Id = 1 };
            context.TechWatchSettings.Add(entity);
        }

        MapDtoToEntity(newSettings, entity);
        await context.SaveChangesAsync(cancellationToken);

        lock (_lock)
        {
            _settings = newSettings;
        }

        logger.LogInformation("Paramètres de la veille technologique enregistrés en base de données SQLite.");
    }

    private TechWatchSettingsDto LoadSettings()
    {
        try
        {
            using var context = dbContextFactory.CreateDbContext();
            var entity = context.TechWatchSettings.Find(1);
            if (entity != null)
            {
                return MapEntityToDto(entity);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement des paramètres de veille techno. Utilisation des valeurs par défaut.");
        }

        return new TechWatchSettingsDto();
    }

    private static TechWatchSettingsDto MapEntityToDto(TechWatchSettingsEntity entity) => new()
    {
        IsEnabled = entity.IsEnabled,
        ScheduleDays = [.. entity.ScheduleDays],
        ScheduleTime = entity.ScheduleTime,
        DestinationType = entity.DestinationType,
        TargetId = entity.TargetId,
        Topics = [.. entity.Topics],
        EnableDevTo = entity.EnableDevTo,
        EnableHackerNews = entity.EnableHackerNews,
        EnableDotnetBlogRss = entity.EnableDotnetBlogRss,
        CustomRssFeeds = [.. entity.CustomRssFeeds],
        GeminiModel = entity.GeminiModel,
        MaxArticles = entity.MaxArticles,
        TranslateToFrench = entity.TranslateToFrench
    };

    private static void MapDtoToEntity(TechWatchSettingsDto dto, TechWatchSettingsEntity entity)
    {
        entity.IsEnabled = dto.IsEnabled;
        entity.ScheduleDays = [.. dto.ScheduleDays];
        entity.ScheduleTime = dto.ScheduleTime;
        entity.DestinationType = dto.DestinationType;
        entity.TargetId = dto.TargetId;
        entity.Topics = [.. dto.Topics];
        entity.EnableDevTo = dto.EnableDevTo;
        entity.EnableHackerNews = dto.EnableHackerNews;
        entity.EnableDotnetBlogRss = dto.EnableDotnetBlogRss;
        entity.CustomRssFeeds = [.. dto.CustomRssFeeds];
        entity.GeminiModel = dto.GeminiModel;
        entity.MaxArticles = dto.MaxArticles;
        entity.TranslateToFrench = dto.TranslateToFrench;
    }
}
