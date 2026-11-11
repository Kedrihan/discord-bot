using System.IO.Abstractions;
using System.Text.Json;
using DiscordBot.DTO;

namespace DiscordBot.Services;

public class TwitchClipHistoryService(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILogger<TwitchClipHistoryService> logger)
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _filePath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", "twitch_clips_history.json");
    private bool _isLoaded;
    private readonly HashSet<string> _seenClipIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _initializedBroadcasters = new(StringComparer.OrdinalIgnoreCase);
    private List<TwitchClipHistoryItemDto> _items = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool IsClipSeen(string clipId)
    {
        if (string.IsNullOrWhiteSpace(clipId)) return false;

        lock (_lock)
        {
            EnsureLoaded();
            return _seenClipIds.Contains(clipId.Trim());
        }
    }

    public bool IsBroadcasterInitialized(string broadcasterId)
    {
        if (string.IsNullOrWhiteSpace(broadcasterId)) return false;

        lock (_lock)
        {
            EnsureLoaded();
            return _initializedBroadcasters.Contains(broadcasterId.Trim());
        }
    }

    public async Task InitializeBroadcasterAsync(string broadcasterId, IEnumerable<string> existingClipIds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(broadcasterId)) return;

        string json;
        lock (_lock)
        {
            EnsureLoaded();
            var trimmedBroadcasterId = broadcasterId.Trim();
            _initializedBroadcasters.Add(trimmedBroadcasterId);

            var now = timeProvider.GetUtcNow();
            foreach (var clipId in existingClipIds)
            {
                if (string.IsNullOrWhiteSpace(clipId)) continue;
                var trimmed = clipId.Trim();
                if (_seenClipIds.Add(trimmed))
                {
                    _items.Add(new TwitchClipHistoryItemDto(trimmed, trimmedBroadcasterId, now));
                }
            }

            PruneOldClips(now);
            json = SerializeStore();
        }

        await SaveToFileAsync(json, ct);
        logger.LogInformation("Broadcaster Twitch {BroadcasterId} initialisé avec protection contre le cold-start.", broadcasterId);
    }

    public async Task MarkClipsAsSeenAsync(IEnumerable<string> clipIds, string broadcasterId, CancellationToken ct = default)
    {
        var clipIdList = clipIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();
        if (clipIdList.Count == 0 && string.IsNullOrWhiteSpace(broadcasterId)) return;

        string json;
        lock (_lock)
        {
            EnsureLoaded();
            var trimmedBroadcasterId = broadcasterId?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(trimmedBroadcasterId))
            {
                _initializedBroadcasters.Add(trimmedBroadcasterId);
            }

            var now = timeProvider.GetUtcNow();
            foreach (var clipId in clipIdList)
            {
                if (_seenClipIds.Add(clipId))
                {
                    _items.Add(new TwitchClipHistoryItemDto(clipId, trimmedBroadcasterId, now));
                }
            }

            PruneOldClips(now);
            json = SerializeStore();
        }

        await SaveToFileAsync(json, ct);
    }

    private void PruneOldClips(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-30);
        var removed = _items.RemoveAll(item => item.DiscoveredAt < cutoff);
        if (_items.Count > 5000)
        {
            _items = _items.TakeLast(5000).ToList();
            removed++;
        }

        if (removed > 0)
        {
            _seenClipIds.Clear();
            foreach (var item in _items)
            {
                _seenClipIds.Add(item.ClipId);
            }
        }
    }

    private string SerializeStore()
    {
        var store = new TwitchClipHistoryStore
        {
            InitializedBroadcasters = [.. _initializedBroadcasters],
            Clips = _items
        };
        return JsonSerializer.Serialize(store, JsonOptions);
    }

    private void EnsureLoaded()
    {
        if (_isLoaded) return;

        try
        {
            if (fileSystem.File.Exists(_filePath))
            {
                var json = fileSystem.File.ReadAllText(_filePath);
                try
                {
                    var store = JsonSerializer.Deserialize<TwitchClipHistoryStore>(json, JsonOptions);
                    if (store != null)
                    {
                        _items = store.Clips ?? [];
                        foreach (var b in store.InitializedBroadcasters ?? [])
                        {
                            if (!string.IsNullOrWhiteSpace(b)) _initializedBroadcasters.Add(b.Trim());
                        }
                        foreach (var item in _items)
                        {
                            if (!string.IsNullOrWhiteSpace(item.ClipId)) _seenClipIds.Add(item.ClipId.Trim());
                            if (!string.IsNullOrWhiteSpace(item.BroadcasterId)) _initializedBroadcasters.Add(item.BroadcasterId.Trim());
                        }
                        _isLoaded = true;
                        return;
                    }
                }
                catch
                {
                    // Fallback to array of items
                    var items = JsonSerializer.Deserialize<List<TwitchClipHistoryItemDto>>(json, JsonOptions);
                    if (items != null)
                    {
                        _items = items;
                        foreach (var item in _items)
                        {
                            if (!string.IsNullOrWhiteSpace(item.ClipId)) _seenClipIds.Add(item.ClipId.Trim());
                            if (!string.IsNullOrWhiteSpace(item.BroadcasterId)) _initializedBroadcasters.Add(item.BroadcasterId.Trim());
                        }
                        _isLoaded = true;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors du chargement de l'historique des clips Twitch. Réinitialisation.");
        }

        _items = [];
        _seenClipIds.Clear();
        _initializedBroadcasters.Clear();
        _isLoaded = true;
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

    private sealed class TwitchClipHistoryStore
    {
        public List<string> InitializedBroadcasters { get; set; } = [];
        public List<TwitchClipHistoryItemDto> Clips { get; set; } = [];
    }
}
