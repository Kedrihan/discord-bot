using DiscordBot.Core.Database;
using DiscordBot.Core.Database.Entities;
using DiscordBot.Core.Database.Services;
using DiscordBot.DTO;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Features.YouTube;

public class YouTubeConfigService(
    IDbContextFactory<BotDbContext> dbContextFactory,
    ILogger<YouTubeConfigService> logger)
{
    private readonly object _lock = new();
    private YouTubeSettingsDto? _settings;

    public YouTubeSettingsDto GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    public async Task<YouTubeSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.YouTubeSettings
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

        return new YouTubeSettingsDto();
    }

    public async Task SaveSettingsAsync(YouTubeSettingsDto newSettings, CancellationToken cancellationToken = default)
    {
        newSettings.CheckIntervalMinutes = Math.Clamp(newSettings.CheckIntervalMinutes, 1, 1440);

        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.YouTubeSettings
            .Include(s => s.Channels)
            .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);

        if (entity == null)
        {
            entity = DatabaseDefaultDataSeeder.CreateDefaultYouTubeSettings();
            context.YouTubeSettings.Add(entity);
        }

        entity.IsEnabled = newSettings.IsEnabled;
        entity.CheckIntervalMinutes = newSettings.CheckIntervalMinutes;

        context.TrackedYouTubeChannels.RemoveRange(entity.Channels);
        entity.Channels = newSettings.Channels.Select(c => new TrackedYouTubeChannelEntity
        {
            YouTubeSettingsId = 1,
            GuildId = c.GuildId?.Trim() ?? string.Empty,
            ChannelId = c.ChannelId?.Trim() ?? string.Empty,
            ChannelTitle = !string.IsNullOrWhiteSpace(c.ChannelTitle) ? c.ChannelTitle.Trim() : (c.ChannelId?.Trim() ?? string.Empty),
            ChannelHandle = c.ChannelHandle?.Trim() ?? string.Empty,
            DiscordChannelId = c.DiscordChannelId?.Trim() ?? string.Empty,
            CustomMessage = c.CustomMessage,
            IsEnabled = c.IsEnabled
        }).ToList();

        await context.SaveChangesAsync(cancellationToken);

        lock (_lock)
        {
            _settings = newSettings;
        }

        logger.LogInformation("Paramètres des notifications YouTube enregistrés en base de données SQLite.");
    }

    private YouTubeSettingsDto LoadSettings()
    {
        try
        {
            using var context = dbContextFactory.CreateDbContext();
            var entity = context.YouTubeSettings
                .Include(s => s.Channels)
                .FirstOrDefault(s => s.Id == 1);

            if (entity != null)
            {
                return MapEntityToDto(entity);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement des paramètres YouTube. Utilisation des valeurs par défaut.");
        }

        return new YouTubeSettingsDto();
    }

    private static YouTubeSettingsDto MapEntityToDto(YouTubeSettingsEntity entity) => new()
    {
        IsEnabled = entity.IsEnabled,
        CheckIntervalMinutes = entity.CheckIntervalMinutes,
        Channels = entity.Channels.Select(c => new TrackedYouTubeChannelDto
        {
            GuildId = c.GuildId,
            ChannelId = c.ChannelId,
            ChannelTitle = c.ChannelTitle,
            ChannelHandle = c.ChannelHandle,
            DiscordChannelId = c.DiscordChannelId,
            CustomMessage = c.CustomMessage,
            IsEnabled = c.IsEnabled
        }).ToList()
    };
}
