using System.Text.Json;
using System.Xml.Linq;
using DiscordBot.DTO;

namespace DiscordBot.Services;

public class NewsAggregatorService(
    HttpClient httpClient,
    SentArticlesHistoryService historyService,
    ILogger<NewsAggregatorService> logger)
{
    public async Task<List<NewsArticleDto>> FetchAllArticlesAsync(TechWatchSettingsDto settings, bool filterDuplicates = true, CancellationToken cancellationToken = default)
    {
        EnsureUserAgent();
        List<NewsArticleDto> articles = [];

        // 1. Dev.to
        if (settings.EnableDevTo)
        {
            var devToArticles = await FetchDevToArticlesAsync(settings.Topics, cancellationToken);
            articles.AddRange(devToArticles);
        }

        // 2. Hacker News (Algolia API)
        if (settings.EnableHackerNews)
        {
            var hnArticles = await FetchHackerNewsArticlesAsync(settings.Topics, cancellationToken);
            articles.AddRange(hnArticles);
        }

        // 3. Blog officiel Microsoft .NET RSS
        if (settings.EnableDotnetBlogRss)
        {
            var dotnetRss = await FetchRssFeedAsync("https://devblogs.microsoft.com/dotnet/feed/", "Microsoft .NET Blog", cancellationToken);
            articles.AddRange(dotnetRss);
        }

        // 4. Flux RSS personnalisés
        if (settings.CustomRssFeeds is { Count: > 0 })
        {
            foreach (var feedUrl in settings.CustomRssFeeds)
            {
                if (string.IsNullOrWhiteSpace(feedUrl))
                {
                    continue;
                }

                var customRss = await FetchRssFeedAsync(feedUrl.Trim(), "RSS", cancellationToken);
                articles.AddRange(customRss);
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
                .Where(a => !historyService.IsAlreadySent(a.Url))
                .ToList();
        }

        return distinctArticles;
    }

    private void EnsureUserAgent()
    {
        if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; DiscordBotTechWatch/1.0)");
        }
    }

    private async Task<List<NewsArticleDto>> FetchDevToArticlesAsync(List<string> topics, CancellationToken cancellationToken)
    {
        List<NewsArticleDto> result = [];
        var selectedTopics = topics.Take(8).ToList();

        foreach (var topic in selectedTopics)
        {
            try
            {
                var url = $"https://dev.to/api/articles?tag={Uri.EscapeDataString(topic)}&per_page=4";
                var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                    var articleUrl = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                    var lang = item.TryGetProperty("language", out var langProp) ? langProp.GetString() : "en";

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(articleUrl))
                    {
                        continue;
                    }

                    result.Add(new NewsArticleDto
                    {
                        Title = title.Trim(),
                        Url = articleUrl.Trim(),
                        Source = $"Dev.to (#{topic})",
                        Language = lang ?? "en"
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erreur lors de la récupération Dev.to pour le sujet {Topic}", topic);
            }
        }

        return result;
    }

    private async Task<List<NewsArticleDto>> FetchHackerNewsArticlesAsync(List<string> topics, CancellationToken cancellationToken)
    {
        List<NewsArticleDto> result = [];
        var sampleTopics = topics.Where(t => t is "ai" or "llm" or "dotnet" or "csharp" or "security" or "architecture").Take(3);

        foreach (var topic in sampleTopics)
        {
            try
            {
                var url = $"https://hn.algolia.com/api/v1/search_by_date?query={Uri.EscapeDataString(topic)}&tags=story&hitsPerPage=3";
                var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!doc.RootElement.TryGetProperty("hits", out var hits))
                {
                    continue;
                }

                foreach (var hit in hits.EnumerateArray())
                {
                    var title = hit.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var articleUrl = hit.TryGetProperty("url", out var u) ? u.GetString() : null;
                    var objectId = hit.TryGetProperty("objectID", out var id) ? id.GetString() : null;

                    if (string.IsNullOrWhiteSpace(articleUrl) && !string.IsNullOrWhiteSpace(objectId))
                    {
                        articleUrl = $"https://news.ycombinator.com/item?id={objectId}";
                    }

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(articleUrl))
                    {
                        continue;
                    }

                    result.Add(new NewsArticleDto
                    {
                        Title = title.Trim(),
                        Url = articleUrl.Trim(),
                        Source = "Hacker News",
                        Language = "en"
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erreur lors de la récupération Hacker News pour le sujet {Topic}", topic);
            }
        }

        return result;
    }

    private async Task<List<NewsArticleDto>> FetchRssFeedAsync(string feedUrl, string sourceName, CancellationToken cancellationToken)
    {
        List<NewsArticleDto> result = [];
        try
        {
            var content = await httpClient.GetStringAsync(feedUrl, cancellationToken);
            var xDoc = XDocument.Parse(content);

            // RSS standard (channel -> item)
            var items = xDoc.Descendants("item");
            foreach (var item in items.Take(4))
            {
                var title = item.Element("title")?.Value;
                var link = item.Element("link")?.Value;

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                result.Add(new NewsArticleDto
                {
                    Title = title.Trim(),
                    Url = link.Trim(),
                    Source = sourceName,
                    Language = "en"
                });
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

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                    {
                        continue;
                    }

                    result.Add(new NewsArticleDto
                    {
                        Title = title.Trim(),
                        Url = link.Trim(),
                        Source = sourceName,
                        Language = "en"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erreur lors de la lecture du flux RSS {FeedUrl}", feedUrl);
        }

        return result;
    }
}
