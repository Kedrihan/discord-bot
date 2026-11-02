using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TechWatchConfigService
{
    private readonly string _filePath;
    private readonly ILogger<TechWatchConfigService> _logger;
    private readonly object _lock = new();
    private TechWatchSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public TechWatchConfigService(IConfiguration configuration, ILogger<TechWatchConfigService> logger)
    {
        _logger = logger;
        
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        _filePath = Path.Combine(dataDir, "techwatch_settings.json");
        _settings = LoadSettings(configuration);
    }

    public TechWatchSettings GetSettings()
    {
        lock (_lock)
        {
            return _settings;
        }
    }

    public async Task SaveSettingsAsync(TechWatchSettings newSettings)
    {
        string json;
        lock (_lock)
        {
            _settings = newSettings;
            json = JsonSerializer.Serialize(_settings, JsonOptions);
        }

        await File.WriteAllTextAsync(_filePath, json);
        _logger.LogInformation("Paramètres de la veille technologique enregistrés dans {Path}", _filePath);
    }

    private TechWatchSettings LoadSettings(IConfiguration configuration)
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<TechWatchSettings>(json);
                if (loaded != null)
                {
                    // Si la clé Gemini est vide dans le json mais présente en variable d'env, on l'injecte
                    if (string.IsNullOrWhiteSpace(loaded.GeminiApiKey))
                    {
                        var envKey = configuration["GEMINI_API_KEY"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                        if (!string.IsNullOrWhiteSpace(envKey))
                        {
                            loaded.GeminiApiKey = envKey;
                        }
                    }
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du chargement des paramètres de veille techno. Utilisation des valeurs par défaut.");
        }

        var defaultSettings = new TechWatchSettings();
        var envApiKey = configuration["GEMINI_API_KEY"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            defaultSettings.GeminiApiKey = envApiKey;
        }

        return defaultSettings;
    }
}
