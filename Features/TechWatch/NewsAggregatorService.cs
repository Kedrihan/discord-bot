using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DiscordBot.DTO.TechWatch;

namespace DiscordBot.Features.TechWatch;

public class NewsAggregatorService(
    HttpClient httpClient,
    SentArticlesHistoryService historyService,
    ILogger<NewsAggregatorService> logger)
{
    private static readonly HashSet<string> AllowedLanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "en",
        "fr"
    };

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

        // Filtrage linguistique (français et anglais uniquement)
        var totalBeforeLanguageFilter = articles.Count;
        articles = articles.Where(IsAllowedLanguage).ToList();
        var filteredOutByLanguage = totalBeforeLanguageFilter - articles.Count;
        if (filteredOutByLanguage > 0)
        {
            logger.LogInformation("{Count} article(s) ignoré(s) car hors français/anglais.", filteredOutByLanguage);
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

    public static bool IsAllowedLanguage(NewsArticleDto article)
    {
        if (article is null || string.IsNullOrWhiteSpace(article.Title))
        {
            return false;
        }

        if (!IsAllowedLanguageCode(article.Language))
        {
            return false;
        }

        if (ContainsUnsupportedScript(article.Title))
        {
            return false;
        }

        return true;
    }

    internal static bool IsAllowedLanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return true;
        }

        var primarySubtag = language.Trim().Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)[0];
        return AllowedLanguageCodes.Contains(primarySubtag);
    }

    internal static bool ContainsUnsupportedScript(string text)
    {
        foreach (var ch in text)
        {
            var isUnsupported = ch is
                (>= '\u3040' and <= '\u309F') or // Hiragana
                (>= '\u30A0' and <= '\u30FF') or // Katakana
                (>= '\uFF66' and <= '\uFF9D') or // Halfwidth Katakana
                (>= '\u3400' and <= '\u4DBF') or // CJK Extension A
                (>= '\u4E00' and <= '\u9FFF') or // CJK Unified Ideographs
                (>= '\uAC00' and <= '\uD7AF') or // Hangul Syllables
                (>= '\u1100' and <= '\u11FF') or // Hangul Jamo
                (>= '\u0400' and <= '\u04FF') or // Cyrillic
                (>= '\u0600' and <= '\u06FF') or // Arabic
                (>= '\u0590' and <= '\u05FF') or // Hebrew
                (>= '\u0900' and <= '\u097F') or // Devanagari
                (>= '\u0E00' and <= '\u0E7F') or // Thai
                (>= '\u0370' and <= '\u03FF');   // Greek

            if (isUnsupported)
            {
                return true;
            }
        }

        return false;
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
                var url = $"https://dev.to/api/articles?tag={Uri.EscapeDataString(topic)}&per_page=8";
                var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var addedForTopic = 0;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                    var articleUrl = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                    var lang = item.TryGetProperty("language", out var langProp) ? langProp.GetString() : null;

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(articleUrl))
                    {
                        continue;
                    }

                    var article = new NewsArticleDto
                    {
                        Title = title.Trim(),
                        Url = articleUrl.Trim(),
                        Source = $"Dev.to (#{topic})",
                        Language = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim()
                    };

                    if (!IsAllowedLanguage(article))
                    {
                        continue;
                    }

                    result.Add(article);
                    addedForTopic++;
                    if (addedForTopic >= 4)
                    {
                        break;
                    }
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

    private const int MaxRssResponseBytes = 5 * 1024 * 1024;

    private async Task<List<NewsArticleDto>> FetchRssFeedAsync(string feedUrl, string sourceName, CancellationToken cancellationToken)
    {
        List<NewsArticleDto> result = [];
        try
        {
            if (!await IsAllowedExternalFeedUriAsync(feedUrl, cancellationToken))
            {
                logger.LogWarning("Flux RSS bloqué (URL invalide ou hôte interne non autorisé) : {FeedUrl}", feedUrl);
                return result;
            }

            var content = await ReadFeedWithLimitAsync(feedUrl, cancellationToken);
            if (content == null) return result;

            var xmlSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxRssResponseBytes
            };
            using var stringReader = new StringReader(content);
            using var xmlReader = XmlReader.Create(stringReader, xmlSettings);
            var xDoc = XDocument.Load(xmlReader);
            var feedLanguage = xDoc.Descendants("channel").Elements("language").FirstOrDefault()?.Value
                ?? xDoc.Root?.Attribute(XNamespace.Xml + "lang")?.Value
                ?? "en";

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
                    Language = feedLanguage.Trim()
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
                        Language = feedLanguage.Trim()
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

    private static async Task<bool> IsAllowedExternalFeedUriAsync(string feedUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(feedUrl?.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return false;

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            return !IsPrivateOrReservedIp(ip);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            return addresses.Length > 0 && addresses.All(addr => !IsPrivateOrReservedIp(addr));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 0
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC;
        }

        return true;
    }

    private async Task<string?> ReadFeedWithLimitAsync(string feedUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        if (response.RequestMessage?.RequestUri is { } finalUri
            && !await IsAllowedExternalFeedUriAsync(finalUri.ToString(), cancellationToken))
        {
            logger.LogWarning("Redirection RSS bloquée vers une URL non autorisée : {FinalUri}", finalUri);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (ms.Length + read > MaxRssResponseBytes)
            {
                logger.LogWarning("Flux RSS trop volumineux (> {MaxBytes} octets) : {FeedUrl}", MaxRssResponseBytes, feedUrl);
                return null;
            }
            ms.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
