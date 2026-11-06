using System.IO.Abstractions;
using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class UptimeStateService(
    IFileSystem fileSystem,
    ILogger<UptimeStateService> logger)
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _filePath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", "uptime_state.json");
    private UptimeState? _state;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public UptimeState GetState()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return new UptimeState
            {
                TargetStates = [.. _state!.TargetStates]
            };
        }
    }

    public UptimeTargetState? GetTargetState(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return null;

        lock (_lock)
        {
            EnsureLoaded();
            return _state!.TargetStates.FirstOrDefault(s => s.TargetId == targetId);
        }
    }

    public async Task UpdateTargetStateAsync(UptimeTargetState targetState, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetState.TargetId)) return;

        string json;
        lock (_lock)
        {
            EnsureLoaded();
            var index = _state!.TargetStates.FindIndex(s => s.TargetId == targetState.TargetId);
            if (index >= 0)
            {
                _state.TargetStates[index] = targetState;
            }
            else
            {
                _state.TargetStates.Add(targetState);
            }

            json = JsonSerializer.Serialize(_state, JsonOptions);
        }

        await SaveToFileAsync(json, cancellationToken);
    }

    public async Task RemoveTargetStateAsync(string targetId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;

        string json;
        lock (_lock)
        {
            EnsureLoaded();
            var removed = _state!.TargetStates.RemoveAll(s => s.TargetId == targetId);
            if (removed == 0) return;

            json = JsonSerializer.Serialize(_state, JsonOptions);
        }

        await SaveToFileAsync(json, cancellationToken);
    }

    private void EnsureLoaded()
    {
        if (_state != null) return;

        try
        {
            if (fileSystem.File.Exists(_filePath))
            {
                var json = fileSystem.File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<UptimeState>(json);
                if (loaded != null)
                {
                    _state = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement de l'état d'uptime. Réinitialisation.");
        }

        _state = new UptimeState();
    }

    private async Task SaveToFileAsync(string json, CancellationToken cancellationToken)
    {
        var dir = fileSystem.Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !fileSystem.Directory.Exists(dir))
        {
            fileSystem.Directory.CreateDirectory(dir);
        }

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await fileSystem.File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
