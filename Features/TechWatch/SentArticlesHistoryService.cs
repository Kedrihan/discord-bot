using System.IO.Abstractions;
using System.Text.Json;

namespace DiscordBot.Services;

public class SentArticleRecord
{
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
}

public class SentArticlesHistoryService(
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILogger<SentArticlesHistoryService> logger)
{
    private readonly object _lock = new();
    private readonly string _filePath = fileSystem.Path.Combine(AppContext.BaseDirectory, "data", "sent_articles.json");
    private List<SentArticleRecord> _records = [];

    public void Initialize()
    {
        var dataDir = fileSystem.Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dataDir) && !fileSystem.Directory.Exists(dataDir))
        {
            fileSystem.Directory.CreateDirectory(dataDir);
        }

        Load();
    }

    public bool IsAlreadySent(string url)
    {
        EnsureInitialized();
        lock (_lock)
        {
            var normalized = NormalizeUrl(url);
            return _records.Any(r => NormalizeUrl(r.Url) == normalized);
        }
    }

    public async Task MarkAsSentAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            foreach (var url in urls)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                _records.Add(new SentArticleRecord
                {
                    Url = url.Trim(),
                    SentAt = now
                });
            }

            // Nettoyage des articles de plus de 30 jours
            var cutoff = now.AddDays(-30);
            _records.RemoveAll(r => r.SentAt < cutoff);
        }

        await SaveAsync(cancellationToken);
    }

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/').ToLowerInvariant();

    private void EnsureInitialized()
    {
        if (fileSystem.File.Exists(_filePath) || _records.Count > 0)
        {
            return;
        }

        Initialize();
    }

    private void Load()
    {
        try
        {
            if (!fileSystem.File.Exists(_filePath))
            {
                _records = [];
                return;
            }

            var json = fileSystem.File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<SentArticleRecord>>(json);
            _records = loaded ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible de charger l'historique des articles envoyés. Réinitialisation.");
            _records = [];
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true });
            }

            await fileSystem.File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'enregistrement de l'historique des articles envoyés.");
        }
    }
}
