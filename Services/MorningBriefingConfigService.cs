using System.IO.Abstractions;
using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class MorningBriefingConfigService(
    IFileSystem fileSystem,
    ILogger<MorningBriefingConfigService> logger)
{
    private readonly object _lock = new();
    private readonly string _filePath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", "morning_briefing_settings.json");
    private MorningBriefingSettings? _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public MorningBriefingSettings GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    public async Task SaveSettingsAsync(MorningBriefingSettings newSettings, CancellationToken cancellationToken = default)
    {
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

        await fileSystem.File.WriteAllTextAsync(_filePath, json, cancellationToken);
        logger.LogInformation("Paramètres du Morning Briefing enregistrés dans {Path}", _filePath);
    }

    private MorningBriefingSettings LoadSettings()
    {
        try
        {
            if (fileSystem.File.Exists(_filePath))
            {
                var json = fileSystem.File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<MorningBriefingSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement des paramètres du Morning Briefing. Utilisation des valeurs par défaut.");
        }

        return new MorningBriefingSettings();
    }
}
