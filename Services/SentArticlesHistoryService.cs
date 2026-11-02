using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class SentArticleRecord
{
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SentArticlesHistoryService
{
    private readonly string _filePath;
    private readonly ILogger<SentArticlesHistoryService> _logger;
    private readonly object _lock = new();
    private List<SentArticleRecord> _records = new();

    public SentArticlesHistoryService(ILogger<SentArticlesHistoryService> logger)
    {
        _logger = logger;
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }

        _filePath = Path.Combine(dataDir, "sent_articles.json");
        Load();
    }

    public bool IsAlreadySent(string url)
    {
        lock (_lock)
        {
            var normalized = NormalizeUrl(url);
            return _records.Any(r => NormalizeUrl(r.Url) == normalized);
        }
    }

    public async Task MarkAsSentAsync(IEnumerable<string> urls)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var url in urls)
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    _records.Add(new SentArticleRecord
                    {
                        Url = url.Trim(),
                        SentAt = now
                    });
                }
            }

            // Nettoyage des articles de plus de 30 jours
            var cutoff = now.AddDays(-30);
            _records.RemoveAll(r => r.SentAt < cutoff);
        }

        await SaveAsync();
    }

    private string NormalizeUrl(string url)
    {
        return url.Trim().TrimEnd('/').ToLowerInvariant();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<List<SentArticleRecord>>(json);
                if (loaded != null)
                {
                    _records = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger l'historique des articles envoyés. Réinitialisation.");
            _records = new List<SentArticleRecord>();
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true });
            }
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'enregistrement de l'historique des articles envoyés.");
        }
    }
}
