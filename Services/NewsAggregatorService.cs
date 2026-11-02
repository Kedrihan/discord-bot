using System.Text.Json;
using System.Xml.Linq;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class NewsAggregatorService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NewsAggregatorService> _logger;
    private readonly SentArticlesHistoryService _historyService;

    public NewsAggregatorService(
        HttpClient httpClient,
        ILogger<NewsAggregatorService> logger,
        SentArticlesHistoryService historyService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _historyService = historyService;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; DiscordBotTechWatch/1.0)");
        }
    }

    public async Task<List<NewsArticle>> FetchAllArticlesAsync(TechWatchSettings settings, bool filterDuplicates = true)
    {
        var articles = new List<NewsArticle>();

        // 1. Dev.to
        if (settings.EnableDevTo)
        {
            var devToArticles = await FetchDevToArticlesAsync(settings.Topics);
            articles.AddRange(devToArticles);
        }

        // 2. Hacker News (Algolia API)
        if (settings.EnableHackerNews)
        {
            var hnArticles = await FetchHackerNewsArticlesAsync(settings.Topics);
            articles.AddRange(hnArticles);
        }

        // 3. Blog officiel Microsoft .NET RSS
        if (settings.EnableDotnetBlogRss)
        {
            var dotnetRss = await FetchRssFeedAsync("https://devblogs.microsoft.com/dotnet/feed/", "Microsoft .NET Blog");
            articles.AddRange(dotnetRss);
        }

        // 4. Flux RSS personnalisés
        if (settings.CustomRssFeeds != null && settings.CustomRssFeeds.Count > 0)
        {
            foreach (var feedUrl in settings.CustomRssFeeds)
            {
                if (!string.IsNullOrWhiteSpace(feedUrl))
                {
                    var customRss = await FetchRssFeedAsync(feedUrl.Trim(), "RSS");
                    articles.AddRange(customRss);
                }
            }
        }

        // Déduplication intra-liste par URL
        var distinctArticles = articles
            .GroupBy(a => a.Url.Trim().TrimEnd('/').ToLowerInvariant())
            .Select(g => g.First())
            .ToList();

        // Filtrage des articles déjà envoyés dans le passé
        if (filterDuplicates)
        {
            distinctArticles = distinctArticles
                .Where(a => !_historyService.IsAlreadySent(a.Url))
                .ToList();
        }

        return distinctArticles;
    }

    private async Task<List<NewsArticle>> FetchDevToArticlesAsync(List<string> topics)
    {
        var result = new List<NewsArticle>();
        var selectedTopics = topics.Take(8).ToList(); // Limite le nombre de requêtes simultanées

        foreach (var topic in selectedTopics)
        {
            try
            {
                var url = $"https://dev.to/api/articles?tag={Uri.EscapeDataString(topic)}&per_page=4";
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var doc = await JsonDocument.ParseAsync(stream);
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                        var articleUrl = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                        var lang = item.TryGetProperty("language", out var langProp) ? langProp.GetString() : "en";

                        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(articleUrl))
                        {
                            result.Add(new NewsArticle
                            {
                                Title = title.Trim(),
                                Url = articleUrl.Trim(),
                                Source = $"Dev.to (#{topic})",
                                Language = lang ?? "en"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la récupération Dev.to pour le sujet {Topic}", topic);
            }
        }

        return result;
    }

    private async Task<List<NewsArticle>> FetchHackerNewsArticlesAsync(List<string> topics)
    {
        var result = new List<NewsArticle>();
        // On cible quelques sujets phares pour HN
        var sampleTopics = topics.Where(t => t is "ai" or "llm" or "dotnet" or "csharp" or "security" or "architecture").Take(3);

        foreach (var topic in sampleTopics)
        {
            try
            {
                var url = $"https://hn.algolia.com/api/v1/search_by_date?query={Uri.EscapeDataString(topic)}&tags=story&hitsPerPage=3";
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var doc = await JsonDocument.ParseAsync(stream);
                    if (doc.RootElement.TryGetProperty("hits", out var hits))
                    {
                        foreach (var hit in hits.EnumerateArray())
                        {
                            var title = hit.TryGetProperty("title", out var t) ? t.GetString() : null;
                            var articleUrl = hit.TryGetProperty("url", out var u) ? u.GetString() : null;
                            var objectId = hit.TryGetProperty("objectID", out var id) ? id.GetString() : null;

                            // Si l'article n'a pas d'URL externe (Ask HN ou Show HN), on redirige vers le thread HN
                            if (string.IsNullOrWhiteSpace(articleUrl) && !string.IsNullOrWhiteSpace(objectId))
                            {
                                articleUrl = $"https://news.ycombinator.com/item?id={objectId}";
                            }

                            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(articleUrl))
                            {
                                result.Add(new NewsArticle
                                {
                                    Title = title.Trim(),
                                    Url = articleUrl.Trim(),
                                    Source = "Hacker News",
                                    Language = "en"
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur lors de la récupération Hacker News pour le sujet {Topic}", topic);
            }
        }

        return result;
    }

    private async Task<List<NewsArticle>> FetchRssFeedAsync(string feedUrl, string sourceName)
    {
        var result = new List<NewsArticle>();
        try
        {
            var content = await _httpClient.GetStringAsync(feedUrl);
            var xDoc = XDocument.Parse(content);

            // RSS standard (channel -> item)
            var items = xDoc.Descendants("item");
            foreach (var item in items.Take(4))
            {
                var title = item.Element("title")?.Value;
                var link = item.Element("link")?.Value;

                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link))
                {
                    result.Add(new NewsArticle
                    {
                        Title = title.Trim(),
                        Url = link.Trim(),
                        Source = sourceName,
                        Language = "en"
                    });
                }
            }

            // Si c'est un flux Atom (entry)
            if (result.Count == 0)
            {
                var entries = xDoc.Descendants().Where(e => e.Name.LocalName == "entry");
                foreach (var entry in entries.Take(4))
                {
                    var title = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;
                    var linkElem = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link");
                    var link = linkElem?.Attribute("href")?.Value ?? linkElem?.Value;

                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link))
                    {
                        result.Add(new NewsArticle
                        {
                            Title = title.Trim(),
                            Url = link.Trim(),
                            Source = sourceName,
                            Language = "en"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors de la lecture du flux RSS {FeedUrl}", feedUrl);
        }

        return result;
    }
}
