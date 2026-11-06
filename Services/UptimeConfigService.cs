using System.IO.Abstractions;
using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class UptimeConfigService(
    IFileSystem fileSystem,
    ILogger<UptimeConfigService> logger)
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _filePath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", "uptime_settings.json");
    private UptimeSettings? _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public event Action? OnSettingsChanged;

    public UptimeSettings GetSettings()
    {
        lock (_lock)
        {
            _settings ??= LoadSettings();
            return _settings;
        }
    }

    public async Task SaveSettingsAsync(UptimeSettings newSettings, CancellationToken cancellationToken = default)
    {
        newSettings.CheckIntervalSeconds = Math.Clamp(newSettings.CheckIntervalSeconds, 10, 86400);

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
            logger.LogInformation("Paramètres d'uptime monitoring enregistrés dans {Path}", _filePath);
        }
        finally
        {
            _fileLock.Release();
        }

        OnSettingsChanged?.Invoke();
    }

    public async Task AddTargetAsync(MonitoredTarget target, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target.Id))
        {
            target.Id = Guid.NewGuid().ToString("N")[..8];
        }

        var current = GetSettings();
        current.Targets.Add(target);
        await SaveSettingsAsync(current, cancellationToken);
    }

    public async Task UpdateTargetAsync(MonitoredTarget target, CancellationToken cancellationToken = default)
    {
        var current = GetSettings();
        var index = current.Targets.FindIndex(t => t.Id == target.Id);
        if (index < 0) return;

        current.Targets[index] = target;
        await SaveSettingsAsync(current, cancellationToken);
    }

    public async Task DeleteTargetAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var current = GetSettings();
        var removed = current.Targets.RemoveAll(t => t.Id == targetId);
        if (removed == 0) return;

        await SaveSettingsAsync(current, cancellationToken);
    }

    public async Task ToggleTargetAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var current = GetSettings();
        var target = current.Targets.FirstOrDefault(t => t.Id == targetId);
        if (target == null) return;

        target.IsEnabled = !target.IsEnabled;
        await SaveSettingsAsync(current, cancellationToken);
    }

    private UptimeSettings LoadSettings()
    {
        try
        {
            if (fileSystem.File.Exists(_filePath))
            {
                var json = fileSystem.File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<UptimeSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement des paramètres d'uptime. Utilisation des valeurs par défaut.");
        }

        return new UptimeSettings();
    }
}
