using System.IO.Abstractions;
using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TwitchConfigService(
    IFileSystem fileSystem,
    IConfiguration configuration,
    ILogger<TwitchConfigService> logger)
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _filePath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", "twitch_settings.json");
    private TwitchSettings? _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public TwitchSettings GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    public async Task SaveSettingsAsync(TwitchSettings newSettings, CancellationToken cancellationToken = default)
    {
        newSettings.CheckIntervalSeconds = Math.Clamp(newSettings.CheckIntervalSeconds, 15, 3600);
        newSettings.ClipCheckIntervalSeconds = Math.Clamp(newSettings.ClipCheckIntervalSeconds, 15, 3600);

        string json;
        lock (_lock)
        {
            _settings = newSettings;
            json = JsonSerializer.Serialize(_settings, JsonOptions);
        }

        var dir = fileSystem.Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !fileSystem.Directory.Exists(dir))
        {
            fileSystem.Directory.CreateDirectory(dir);
        }

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await fileSystem.File.WriteAllTextAsync(_filePath, json, cancellationToken);
            logger.LogInformation("Paramètres des notifications Twitch enregistrés dans {Path}", _filePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private TwitchSettings LoadSettings()
    {
        try
        {
            if (fileSystem.File.Exists(_filePath))
            {
                var json = fileSystem.File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<TwitchSettings>(json);
                if (loaded != null)
                {
                    ApplyEnvironmentFallbacks(loaded);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement des paramètres Twitch. Utilisation des valeurs par défaut.");
        }

        var defaultSettings = new TwitchSettings();
        ApplyEnvironmentFallbacks(defaultSettings);
        return defaultSettings;
    }

    private void ApplyEnvironmentFallbacks(TwitchSettings target)
    {
        if (string.IsNullOrWhiteSpace(target.ClientId))
        {
            var envClientId = configuration["TWITCH_CLIENT_ID"] ?? Environment.GetEnvironmentVariable("TWITCH_CLIENT_ID");
            if (!string.IsNullOrWhiteSpace(envClientId))
            {
                target.ClientId = envClientId;
            }
        }

        if (string.IsNullOrWhiteSpace(target.ClientSecret))
        {
            var envClientSecret = configuration["TWITCH_CLIENT_SECRET"] ?? Environment.GetEnvironmentVariable("TWITCH_CLIENT_SECRET");
            if (!string.IsNullOrWhiteSpace(envClientSecret))
            {
                target.ClientSecret = envClientSecret;
            }
        }

        if (string.IsNullOrWhiteSpace(target.UserAccessToken))
        {
            var envUserAccessToken = configuration["TWITCH_USER_ACCESS_TOKEN"] ?? Environment.GetEnvironmentVariable("TWITCH_USER_ACCESS_TOKEN");
            if (!string.IsNullOrWhiteSpace(envUserAccessToken))
            {
                target.UserAccessToken = envUserAccessToken;
            }
        }
    }
}
