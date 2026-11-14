using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DiscordBot.DTO;

namespace DiscordBot.Features.YouTube;

public partial class YouTubeFeedClient(
    HttpClient httpClient,
    ILogger<YouTubeFeedClient> logger)
{
    private const int MaxResponseSizeBytes = 5 * 1024 * 1024; // 5 Mo max pour éviter le DoS mémoire
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace YtNs = "http://www.youtube.com/xml/schemas/2015";
    private static readonly XNamespace MediaNs = "http://search.yahoo.com/mrss/";

    private static readonly XmlReaderSettings SafeXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxResponseSizeBytes
    };

    [GeneratedRegex(@"^UC[a-zA-Z0-9_-]{20,30}$", RegexOptions.Compiled)]
    private static partial Regex CanonicalChannelIdRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]{8,32}$", RegexOptions.Compiled)]
    private static partial Regex ValidVideoIdRegex();

    [GeneratedRegex(@"^@?[a-zA-Z0-9_.-]{1,50}$", RegexOptions.Compiled)]
    private static partial Regex ValidHandleRegex();

    [GeneratedRegex(@"itemprop=[""'](?:identifier|channelId)[""']\s+content=[""'](UC[a-zA-Z0-9_-]{20,30})[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex MetaChannelIdRegex();

    [GeneratedRegex(@"""(?:externalId|channelId)""\s*:\s*""(UC[a-zA-Z0-9_-]{20,30})""", RegexOptions.Compiled)]
    private static partial Regex JsonChannelIdRegex();

    [GeneratedRegex(@"href=[""']https://(?:www\.)?youtube\.com/channel/(UC[a-zA-Z0-9_-]{20,30})[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CanonicalLinkRegex();

    [GeneratedRegex(@"<meta\s+property=[""']og:title[""']\s+content=[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex OgTitleRegex();

    [GeneratedRegex(@"<title>([^<]+?)(?:\s*-\s*YouTube)?</title>", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTitleRegex();

    public static bool IsAllowedYouTubeHost(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!uri.IsDefaultPort && uri.Port != 443) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

        var host = uri.Host.ToLowerInvariant();
        return host is "youtube.com" or "www.youtube.com" or "m.youtube.com";
    }

    public static bool IsAllowedYouTubeVideoHost(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!uri.IsDefaultPort && uri.Port != 443) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

        var host = uri.Host.ToLowerInvariant();
        return host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be";
    }

    public static bool IsAllowedYouTubeImageHost(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

        var host = uri.Host.ToLowerInvariant();
        return host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("ytimg.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("ggpht.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith("img.yt", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<YouTubeVideoInfoDto>> GetChannelVideosAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return [];

        var trimmedChannelId = channelId.Trim();
        var feedUrl = $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(trimmedChannelId)}";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));

            using var response = await httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Échec de la récupération du flux Atom YouTube pour la chaîne {ChannelId}. Statut : {StatusCode}", channelId, response.StatusCode);
                return [];
            }

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri != null && !IsAllowedYouTubeHost(finalUri))
            {
                logger.LogWarning("Redirection non autorisée bloquée pour la chaîne {ChannelId} vers : {FinalUri}", channelId, finalUri);
                return [];
            }

            var content = await ReadStringWithLimitAsync(response.Content, MaxResponseSizeBytes, cts.Token);
            return ParseAtomFeed(content, trimmedChannelId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la lecture du flux Atom YouTube pour la chaîne {ChannelId}.", channelId);
            return [];
        }
    }

    public async Task<YouTubeChannelResolutionResultDto> ResolveChannelIdAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new YouTubeChannelResolutionResultDto(false, null, null, "L'identifiant ou l'URL de la chaîne ne peut pas être vide.");
        }

        var clean = input.Trim();

        // 1. Direct canonical UC... ID
        if (CanonicalChannelIdRegex().IsMatch(clean))
        {
            var title = await FetchFeedTitleAsync(clean, ct);
            return new YouTubeChannelResolutionResultDto(true, clean, title ?? clean, null);
        }

        // 2. URL resolution or handle validation
        string targetUrl;
        if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(clean, UriKind.Absolute, out var parsedUri) || parsedUri.Scheme != Uri.UriSchemeHttps)
            {
                return new YouTubeChannelResolutionResultDto(false, null, null, "Seules les URLs HTTPS YouTube sont autorisées.");
            }

            if (!IsAllowedYouTubeHost(parsedUri))
            {
                return new YouTubeChannelResolutionResultDto(false, null, null, "Domaine non autorisé. Seul le domaine officiel YouTube est accepté.");
            }

            if (parsedUri.AbsolutePath.StartsWith("/redirect", StringComparison.OrdinalIgnoreCase))
            {
                return new YouTubeChannelResolutionResultDto(false, null, null, "Les URLs de redirection YouTube ne sont pas autorisées.");
            }

            // Direct /channel/UC... URL
            if (parsedUri.AbsolutePath.StartsWith("/channel/", StringComparison.OrdinalIgnoreCase))
            {
                var candidateId = parsedUri.AbsolutePath["/channel/".Length..].TrimEnd('/');
                if (CanonicalChannelIdRegex().IsMatch(candidateId))
                {
                    var title = await FetchFeedTitleAsync(candidateId, ct);
                    return new YouTubeChannelResolutionResultDto(true, candidateId, title ?? candidateId, null);
                }
            }

            targetUrl = $"https://www.youtube.com{parsedUri.AbsolutePath}";
        }
        else
        {
            if (!ValidHandleRegex().IsMatch(clean))
            {
                return new YouTubeChannelResolutionResultDto(false, null, null, "Format d'identifiant ou de handle YouTube invalide.");
            }

            var handle = clean.StartsWith('@') ? clean : $"@{clean}";
            targetUrl = $"https://www.youtube.com/{handle}";
        }

        // 3. Fetch HTML page to resolve canonical UC... ID
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));

            using var response = await httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new YouTubeChannelResolutionResultDto(false, null, null, $"Impossible d'accéder à la page YouTube (Statut : {(int)response.StatusCode}).");
            }

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri != null && !IsAllowedYouTubeHost(finalUri))
            {
                logger.LogWarning("Redirection non autorisée bloquée lors de la résolution vers : {FinalUri}", finalUri);
                return new YouTubeChannelResolutionResultDto(false, null, null, "Redirection non autorisée.");
            }

            var html = await ReadStringWithLimitAsync(response.Content, MaxResponseSizeBytes, cts.Token);

            var channelId = ExtractChannelIdFromHtml(html);
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return new YouTubeChannelResolutionResultDto(false, null, null, "Impossible de trouver l'identifiant canonique (UC...) de la chaîne.");
            }

            var channelTitle = ExtractChannelTitleFromHtml(html);
            if (string.IsNullOrWhiteSpace(channelTitle))
            {
                channelTitle = await FetchFeedTitleAsync(channelId, ct) ?? channelId;
            }

            return new YouTubeChannelResolutionResultDto(true, channelId, channelTitle, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var safeInput = (input.Length > 100 ? input[..100] : input).Replace("\r", "").Replace("\n", "");
            logger.LogError(ex, "Erreur lors de la résolution de la chaîne YouTube : {Input}", safeInput);
            return new YouTubeChannelResolutionResultDto(false, null, null, $"Erreur lors de la résolution : {ex.Message}");
        }
    }

    public static IReadOnlyList<YouTubeVideoInfoDto> ParseAtomFeed(string xmlContent, string defaultChannelId)
    {
        if (string.IsNullOrWhiteSpace(xmlContent)) return [];

        try
        {
            using var stringReader = new StringReader(xmlContent);
            using var xmlReader = XmlReader.Create(stringReader, SafeXmlSettings);
            var doc = XDocument.Load(xmlReader);
            var feed = doc.Root;
            if (feed == null) return [];

            var feedTitle = feed.Element(AtomNs + "title")?.Value?.Trim() ?? string.Empty;
            var entries = feed.Elements(AtomNs + "entry");
            var videos = new List<YouTubeVideoInfoDto>();

            foreach (var entry in entries)
            {
                var videoId = entry.Element(YtNs + "videoId")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(videoId))
                {
                    var idElem = entry.Element(AtomNs + "id")?.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(idElem) && idElem.StartsWith("yt:video:", StringComparison.OrdinalIgnoreCase))
                    {
                        videoId = idElem["yt:video:".Length..].Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(videoId) || !ValidVideoIdRegex().IsMatch(videoId)) continue;

                var channelId = entry.Element(YtNs + "channelId")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(channelId) || !CanonicalChannelIdRegex().IsMatch(channelId))
                {
                    channelId = defaultChannelId;
                }

                var title = entry.Element(AtomNs + "title")?.Value?.Trim() ?? string.Empty;
                var alternateLink = entry.Elements(AtomNs + "link")
                    .FirstOrDefault(l => l.Attribute("rel")?.Value == "alternate")
                    ?.Attribute("href")?.Value?.Trim();

                var videoUrl = (!string.IsNullOrWhiteSpace(alternateLink) &&
                                Uri.TryCreate(alternateLink, UriKind.Absolute, out var altUri) &&
                                altUri.Scheme == Uri.UriSchemeHttps &&
                                IsAllowedYouTubeVideoHost(altUri))
                    ? alternateLink
                    : $"https://www.youtube.com/watch?v={videoId}";

                var authorName = entry.Element(AtomNs + "author")?.Element(AtomNs + "name")?.Value?.Trim();
                var channelTitle = !string.IsNullOrWhiteSpace(authorName) ? authorName : feedTitle;

                var publishedStr = entry.Element(AtomNs + "published")?.Value;
                var publishedAt = DateTimeOffset.TryParse(publishedStr, out var parsedDate)
                    ? parsedDate
                    : DateTimeOffset.UtcNow;

                var mediaGroup = entry.Element(MediaNs + "group");
                var rawThumbnail = mediaGroup?.Element(MediaNs + "thumbnail")?.Attribute("url")?.Value?.Trim();
                var thumbnailUrl = (!string.IsNullOrWhiteSpace(rawThumbnail) &&
                                    Uri.TryCreate(rawThumbnail, UriKind.Absolute, out var thumbUri) &&
                                    thumbUri.Scheme == Uri.UriSchemeHttps &&
                                    IsAllowedYouTubeImageHost(thumbUri))
                    ? rawThumbnail
                    : $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";

                var description = mediaGroup?.Element(MediaNs + "description")?.Value ?? string.Empty;

                var isShort = videoUrl.Contains("/shorts/", StringComparison.OrdinalIgnoreCase) ||
                              title.Contains("#shorts", StringComparison.OrdinalIgnoreCase) ||
                              description.Contains("#shorts", StringComparison.OrdinalIgnoreCase);

                var videoType = isShort ? "Short" : "Vidéo";

                videos.Add(new YouTubeVideoInfoDto(
                    VideoId: videoId,
                    ChannelId: channelId,
                    ChannelTitle: channelTitle,
                    Title: title,
                    Url: videoUrl,
                    ThumbnailUrl: thumbnailUrl,
                    PublishedAt: publishedAt,
                    VideoType: videoType
                ));
            }

            return videos;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private async Task<string?> FetchFeedTitleAsync(string channelId, CancellationToken ct)
    {
        try
        {
            var feedUrl = $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(channelId)}";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DefaultTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));

            using var response = await httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri != null && !IsAllowedYouTubeHost(finalUri)) return null;

            var xml = await ReadStringWithLimitAsync(response.Content, MaxResponseSizeBytes, cts.Token);
            using var stringReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(stringReader, SafeXmlSettings);
            var doc = XDocument.Load(xmlReader);
            return doc.Root?.Element(AtomNs + "title")?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static string? ExtractChannelIdFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var metaMatch = MetaChannelIdRegex().Match(html);
        if (metaMatch.Success) return metaMatch.Groups[1].Value;

        var jsonMatch = JsonChannelIdRegex().Match(html);
        if (jsonMatch.Success) return jsonMatch.Groups[1].Value;

        var linkMatch = CanonicalLinkRegex().Match(html);
        if (linkMatch.Success) return linkMatch.Groups[1].Value;

        return null;
    }

    public static string? ExtractChannelTitleFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var ogMatch = OgTitleRegex().Match(html);
        if (ogMatch.Success)
        {
            var title = WebUtility.HtmlDecode(ogMatch.Groups[1].Value).Trim();
            if (!string.IsNullOrEmpty(title)) return title;
        }

        var titleMatch = HtmlTitleRegex().Match(html);
        if (titleMatch.Success)
        {
            var title = WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
            if (!string.IsNullOrEmpty(title)) return title;
        }

        return null;
    }

    private static async Task<string> ReadStringWithLimitAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value > maxBytes)
        {
            throw new InvalidOperationException($"La réponse dépasse la taille maximale autorisée ({maxBytes} octets).");
        }

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[8192];
        var sb = new StringBuilder();
        int charsRead;
        int totalBytes = 0;

        while ((charsRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            totalBytes += charsRead * sizeof(char);
            if (totalBytes > maxBytes)
            {
                throw new InvalidOperationException($"La réponse dépasse la taille maximale autorisée ({maxBytes} octets).");
            }
            sb.Append(buffer, 0, charsRead);
        }

        return sb.ToString();
    }
}
