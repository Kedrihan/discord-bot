using System.IO.Abstractions;
using System.Text.Json;
using DiscordBot.DTO;

namespace DiscordBot.Services;

public class TechWatchConfigService(
    IFileSystem fileSystem,
    IConfiguration configuration,
    ILogger<TechWatchConfigService> logger)
{
    private readonly object _lock = new();
    private readonly string _filePath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", "techwatch_settings.json");
    private TechWatchSettingsDto? _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public TechWatchSettingsDto GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    public async Task SaveSettingsAsync(TechWatchSettingsDto newSettings, CancellationToken cancellationToken = default)
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
        logger.LogInformation("Paramètres de la veille technologique enregistrés dans {Path}", _filePath);
    }

    private TechWatchSettingsDto LoadSettings()
    {
        try
        {
            if (fileSystem.File.Exists(_filePath))
            {
                var json = fileSystem.File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<TechWatchSettingsDto>(json);
                if (loaded != null)
                {
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
            logger.LogError(ex, "Erreur lors du chargement des paramètres de veille techno. Utilisation des valeurs par défaut.");
        }

        var defaultSettings = new TechWatchSettingsDto();
        var envApiKey = configuration["GEMINI_API_KEY"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envApiKey))
        {
            defaultSettings.GeminiApiKey = envApiKey;
        }

        return defaultSettings;
    }
}
