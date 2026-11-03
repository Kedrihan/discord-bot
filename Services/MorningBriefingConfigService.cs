using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class MorningBriefingConfigService
{
    private readonly string _filePath;
    private readonly ILogger<MorningBriefingConfigService> _logger;
    private readonly object _lock = new();
    private MorningBriefingSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public MorningBriefingConfigService(ILogger<MorningBriefingConfigService> logger)
    {
        _logger = logger;
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        _filePath = Path.Combine(dataDir, "morning_briefing_settings.json");
        _settings = LoadSettings();
    }

    public MorningBriefingSettings GetSettings()
    {
        lock (_lock)
        {
            return _settings;
        }
    }

    public async Task SaveSettingsAsync(MorningBriefingSettings newSettings)
    {
        string json;
        lock (_lock)
        {
            _settings = newSettings;
            json = JsonSerializer.Serialize(_settings, JsonOptions);
        }

        await File.WriteAllTextAsync(_filePath, json);
        _logger.LogInformation("Paramètres du Morning Briefing enregistrés dans {Path}", _filePath);
    }

    private MorningBriefingSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<MorningBriefingSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement des paramètres du Morning Briefing. Utilisation des valeurs par défaut.");
        }

        return new MorningBriefingSettings();
    }
}
