using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using DiscordBot.DTO;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Services;

public class TwitchConfigService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger<TwitchConfigService> logger)
{
    private readonly object _lock = new();
    private TwitchSettingsDto? _settings;

    public TwitchConfigService(
        IDbContextFactory<BotDbContext> dbContextFactory,
        IConfiguration? configuration,
        ILogger<TwitchConfigService> logger) : this(dbContextFactory, logger)
    {
    }

    public TwitchSettingsDto GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    public async Task<TwitchSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TwitchSettings
            .Include(s => s.Channels)
            .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);

        if (entity != null)
        {
            var dto = MapEntityToDto(entity);
            lock (_lock)
            {
                _settings = dto;
            }
            return dto;
        }

        return new TwitchSettingsDto();
    }

    public async Task SaveSettingsAsync(TwitchSettingsDto newSettings, CancellationToken cancellationToken = default)
    {
        newSettings.CheckIntervalSeconds = Math.Clamp(newSettings.CheckIntervalSeconds, 15, 3600);
        newSettings.ClipCheckIntervalSeconds = Math.Clamp(newSettings.ClipCheckIntervalSeconds, 15, 3600);

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.TwitchSettings
            .Include(s => s.Channels)
            .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);

        if (entity == null)
        {
            entity = new TwitchSettingsEntity { Id = 1 };
            context.TwitchSettings.Add(entity);
        }

        entity.IsEnabled = newSettings.IsEnabled;
        entity.IsClipsEnabled = newSettings.IsClipsEnabled;
        entity.CheckIntervalSeconds = newSettings.CheckIntervalSeconds;
        entity.ClipCheckIntervalSeconds = newSettings.ClipCheckIntervalSeconds;

        context.TrackedTwitchChannels.RemoveRange(entity.Channels);
        entity.Channels = newSettings.Channels.Select(c => new TrackedTwitchChannelEntity
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

        await context.SaveChangesAsync(cancellationToken);

        lock (_lock)
        {
            _settings = newSettings;
        }

        logger.LogInformation("Paramètres des notifications Twitch enregistrés en base de données SQLite.");
    }

    private TwitchSettingsDto LoadSettings()
    {
        try
        {
            using var context = dbContextFactory.CreateDbContext();
            var entity = context.TwitchSettings
                .Include(s => s.Channels)
                .FirstOrDefault(s => s.Id == 1);

            if (entity != null)
            {
                return MapEntityToDto(entity);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement des paramètres Twitch. Utilisation des valeurs par défaut.");
        }

        return new TwitchSettingsDto();
    }

    private static TwitchSettingsDto MapEntityToDto(TwitchSettingsEntity entity) => new()
    {
        IsEnabled = entity.IsEnabled,
        IsClipsEnabled = entity.IsClipsEnabled,
        CheckIntervalSeconds = entity.CheckIntervalSeconds,
        ClipCheckIntervalSeconds = entity.ClipCheckIntervalSeconds,
        Channels = entity.Channels.Select(c => new TrackedTwitchChannelDto
        {
            TwitchUsername = c.TwitchUsername,
            DiscordChannelId = c.DiscordChannelId,
            CustomMessage = c.CustomMessage,
            IsEnabled = c.IsEnabled,
            IsClipEnabled = c.IsClipEnabled,
            ClipDiscordChannelId = c.ClipDiscordChannelId,
            ClipCustomMessage = c.ClipCustomMessage
        }).ToList()
    };
}
