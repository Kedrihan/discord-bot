using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using DiscordBot.DTO;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Services;

public class MorningBriefingConfigService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger<MorningBriefingConfigService> logger)
{
    private readonly object _lock = new();
    private MorningBriefingSettingsDto? _settings;

    public MorningBriefingSettingsDto GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    public async Task<MorningBriefingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.MorningBriefingSettings.FindAsync([1], cancellationToken);
        if (entity != null)
        {
            var dto = MapEntityToDto(entity);
            lock (_lock)
            {
                _settings = dto;
            }
            return dto;
        }

        return new MorningBriefingSettingsDto();
    }

    public async Task SaveSettingsAsync(MorningBriefingSettingsDto newSettings, CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.MorningBriefingSettings.FindAsync([1], cancellationToken);
        if (entity == null)
        {
            entity = new MorningBriefingSettingsEntity { Id = 1 };
            context.MorningBriefingSettings.Add(entity);
        }

        MapDtoToEntity(newSettings, entity);
        await context.SaveChangesAsync(cancellationToken);

        lock (_lock)
        {
            _settings = newSettings;
        }

        logger.LogInformation("Paramètres du Morning Briefing enregistrés en base de données SQLite.");
    }

    private MorningBriefingSettingsDto LoadSettings()
    {
        try
        {
            using var context = dbContextFactory.CreateDbContext();
            var entity = context.MorningBriefingSettings.Find(1);
            if (entity != null)
            {
                return MapEntityToDto(entity);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement des paramètres du Morning Briefing. Utilisation des valeurs par défaut.");
        }

        return new MorningBriefingSettingsDto();
    }

    private static MorningBriefingSettingsDto MapEntityToDto(MorningBriefingSettingsEntity entity) => new()
    {
        IsEnabled = entity.IsEnabled,
        ScheduleDays = [.. entity.ScheduleDays],
        ScheduleTime = entity.ScheduleTime,
        DestinationType = entity.DestinationType,
        TargetId = entity.TargetId,
        Address = entity.Address,
        Latitude = entity.Latitude,
        Longitude = entity.Longitude,
        MorningStartHour = entity.MorningStartHour,
        MorningEndHour = entity.MorningEndHour,
        EveningStartHour = entity.EveningStartHour,
        EveningEndHour = entity.EveningEndHour
    };

    private static void MapDtoToEntity(MorningBriefingSettingsDto dto, MorningBriefingSettingsEntity entity)
    {
        entity.IsEnabled = dto.IsEnabled;
        entity.ScheduleDays = [.. dto.ScheduleDays];
        entity.ScheduleTime = dto.ScheduleTime;
        entity.DestinationType = dto.DestinationType;
        entity.TargetId = dto.TargetId;
        entity.Address = dto.Address;
        entity.Latitude = dto.Latitude;
        entity.Longitude = dto.Longitude;
        entity.MorningStartHour = dto.MorningStartHour;
        entity.MorningEndHour = dto.MorningEndHour;
        entity.EveningStartHour = dto.EveningStartHour;
        entity.EveningEndHour = dto.EveningEndHour;
    }
}
