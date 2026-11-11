using System.IO.Abstractions;
using System.Text.Json;
using DiscordBot.DTO;

namespace DiscordBot.Services;

public class TwitchStateService(
    IFileSystem fileSystem,
    ILogger<TwitchStateService> logger)
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _filePath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", "twitch_live_state.json");
    private List<TwitchActiveStreamStateDto>? _activeStreams;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public IReadOnlyList<TwitchActiveStreamStateDto> GetActiveStreams()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return [.. _activeStreams!];
        }
    }

    public TwitchActiveStreamStateDto? GetActiveStream(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;

        lock (_lock)
        {
            EnsureLoaded();
            return _activeStreams!.FirstOrDefault(s => string.Equals(s.TwitchUsername, username, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task SetActiveStreamAsync(TwitchActiveStreamStateDto state, CancellationToken ct = default)
    {
        string json;
        lock (_lock)
        {
            EnsureLoaded();
            var index = _activeStreams!.FindIndex(s => string.Equals(s.TwitchUsername, state.TwitchUsername, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _activeStreams[index] = state;
            }
            else
            {
                _activeStreams.Add(state);
            }

            json = JsonSerializer.Serialize(_activeStreams, JsonOptions);
        }

        await SaveToFileAsync(json, ct);
        logger.LogInformation("État du stream actif pour {Username} mis à jour (Message ID: {MessageId}).", state.TwitchUsername, state.DiscordMessageId);
    }

    public async Task RemoveActiveStreamAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return;

        string json;
        lock (_lock)
        {
            EnsureLoaded();
            var removed = _activeStreams!.RemoveAll(s => string.Equals(s.TwitchUsername, username, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return;

            json = JsonSerializer.Serialize(_activeStreams, JsonOptions);
        }

        await SaveToFileAsync(json, ct);
        logger.LogInformation("Stream actif retiré pour le streamer {Username}.", username);
    }

    private void EnsureLoaded()
    {
        if (_activeStreams != null) return;

        try
        {
            if (fileSystem.File.Exists(_filePath))
            {
                var json = fileSystem.File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<List<TwitchActiveStreamStateDto>>(json);
                if (loaded != null)
                {
                    _activeStreams = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement de l'état des streams actifs Twitch. Réinitialisation.");
        }

        _activeStreams = [];
    }

    private async Task SaveToFileAsync(string json, CancellationToken ct)
    {
        var dir = fileSystem.Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !fileSystem.Directory.Exists(dir))
        {
            fileSystem.Directory.CreateDirectory(dir);
        }

        await _fileLock.WaitAsync(ct);
        try
        {
            await fileSystem.File.WriteAllTextAsync(_filePath, json, ct);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
