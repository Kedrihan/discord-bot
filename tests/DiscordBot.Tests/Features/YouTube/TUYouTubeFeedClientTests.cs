using System.Net;
using DiscordBot.Features.YouTube;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Features.YouTube;

[TestFixture]
public class TUYouTubeFeedClientTests
{
    private const string SampleFeedXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns:media="http://search.yahoo.com/mrss/">
          <link rel="self" href="http://www.youtube.com/feeds/videos.xml?channel_id=UC1234567890abcdefghijk"/>
          <id>yt:channel:UC1234567890abcdefghijk</id>
          <yt:channelId>UC1234567890abcdefghijk</yt:channelId>
          <title>Underscore_</title>
          <link rel="alternate" href="https://www.youtube.com/channel/UC1234567890abcdefghijk"/>
          <author>
            <name>Underscore_</name>
            <uri>https://www.youtube.com/channel/UC1234567890abcdefghijk</uri>
          </author>
          <published>2026-11-01T12:00:00+00:00</published>
          <entry>
            <id>yt:video:vid12345678</id>
            <yt:videoId>vid12345678</yt:videoId>
            <yt:channelId>UC1234567890abcdefghijk</yt:channelId>
            <title>Le meilleur épisode de la saison !</title>
            <link rel="alternate" href="https://www.youtube.com/watch?v=vid12345678"/>
            <author>
              <name>Underscore_</name>
              <uri>https://www.youtube.com/channel/UC1234567890abcdefghijk</uri>
            </author>
            <published>2026-11-14T08:00:00+00:00</published>
            <updated>2026-11-14T08:05:00+00:00</updated>
            <media:group>
              <media:title>Le meilleur épisode de la saison !</media:title>
              <media:content url="https://www.youtube.com/v/vid12345678?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
              <media:thumbnail url="https://i.ytimg.com/vi/vid12345678/hqdefault.jpg" width="480" height="360"/>
              <media:description>Une superbe vidéo complète.</media:description>
            </media:group>
          </entry>
          <entry>
            <id>yt:video:short12345</id>
            <yt:videoId>short12345</yt:videoId>
            <yt:channelId>UC1234567890abcdefghijk</yt:channelId>
            <title>Incroyable moment en direct #shorts</title>
            <link rel="alternate" href="https://www.youtube.com/shorts/short12345"/>
            <author>
              <name>Underscore_</name>
              <uri>https://www.youtube.com/channel/UC1234567890abcdefghijk</uri>
            </author>
            <published>2026-11-14T09:00:00+00:00</published>
            <updated>2026-11-14T09:05:00+00:00</updated>
            <media:group>
              <media:title>Incroyable moment en direct #shorts</media:title>
              <media:thumbnail url="https://i.ytimg.com/vi/short12345/hqdefault.jpg" width="480" height="360"/>
              <media:description>Extrait court #shorts</media:description>
            </media:group>
          </entry>
        </feed>
        """;

    private const string SampleHtml = """
        <!DOCTYPE html>
        <html>
        <head>
          <title>Underscore_ - YouTube</title>
          <meta property="og:title" content="Underscore_">
          <meta itemprop="identifier" content="UC1234567890abcdefghijk">
          <link rel="canonical" href="https://www.youtube.com/channel/UC1234567890abcdefghijk">
        </head>
        <body>
          <div>YouTube Channel Page</div>
        </body>
        </html>
        """;

    [Test]
    public void ParseAtomFeed_ShouldExtractVideoDetails_AndIdentifyShorts()
    {
        // Act
        var videos = YouTubeFeedClient.ParseAtomFeed(SampleFeedXml, "UC1234567890abcdefghijk");

        // Assert
        videos.Should().HaveCount(2);

        var normalVideo = videos[0];
        normalVideo.VideoId.Should().Be("vid12345678");
        normalVideo.ChannelId.Should().Be("UC1234567890abcdefghijk");
        normalVideo.ChannelTitle.Should().Be("Underscore_");
        normalVideo.Title.Should().Be("Le meilleur épisode de la saison !");
        normalVideo.Url.Should().Be("https://www.youtube.com/watch?v=vid12345678");
        normalVideo.ThumbnailUrl.Should().Be("https://i.ytimg.com/vi/vid12345678/hqdefault.jpg");
        normalVideo.VideoType.Should().Be("Vidéo");

        var shortVideo = videos[1];
        shortVideo.VideoId.Should().Be("short12345");
        shortVideo.Title.Should().Be("Incroyable moment en direct #shorts");
        shortVideo.Url.Should().Be("https://www.youtube.com/shorts/short12345");
        shortVideo.VideoType.Should().Be("Short");
    }

    [Test]
    public void ParseAtomFeed_ShouldReturnEmpty_WhenXmlIsNullOrMalformed()
    {
        // Act & Assert
        YouTubeFeedClient.ParseAtomFeed(string.Empty, "UC_default").Should().BeEmpty();
        YouTubeFeedClient.ParseAtomFeed("   ", "UC_default").Should().BeEmpty();
        YouTubeFeedClient.ParseAtomFeed("invalid xml content <not-closed", "UC_default").Should().BeEmpty();
    }

    [Test]
    public void ExtractChannelIdFromHtml_ShouldFindId_FromMetaOrJsonOrLink()
    {
        // Meta tag
        YouTubeFeedClient.ExtractChannelIdFromHtml(SampleHtml).Should().Be("UC1234567890abcdefghijk");

        // JSON format
        var jsonHtml = """var ytInitialData = {"externalId":"UC99988877766655544433322"};""";
        YouTubeFeedClient.ExtractChannelIdFromHtml(jsonHtml).Should().Be("UC99988877766655544433322");

        // Canonical link
        var linkHtml = """<link rel="canonical" href="https://www.youtube.com/channel/UCabcdef123456789012345">""";
        YouTubeFeedClient.ExtractChannelIdFromHtml(linkHtml).Should().Be("UCabcdef123456789012345");

        // Null / Empty
        YouTubeFeedClient.ExtractChannelIdFromHtml("").Should().BeNull();
        YouTubeFeedClient.ExtractChannelIdFromHtml("<html><body>No ID here</body></html>").Should().BeNull();
    }

    [Test]
    public void ExtractChannelTitleFromHtml_ShouldFindTitle_FromOgTitleOrTitleTag()
    {
        YouTubeFeedClient.ExtractChannelTitleFromHtml(SampleHtml).Should().Be("Underscore_");

        var titleOnlyHtml = "<title>Mon Super Titre - YouTube</title>";
        YouTubeFeedClient.ExtractChannelTitleFromHtml(titleOnlyHtml).Should().Be("Mon Super Titre");

        YouTubeFeedClient.ExtractChannelTitleFromHtml("").Should().BeNull();
    }

    [Test]
    public void IsAllowedYouTubeHost_ShouldOnlyAllow_OfficialHttpsYouTubeDomains()
    {
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("https://www.youtube.com/@handle")).Should().BeTrue();
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("https://youtube.com/channel/UC123")).Should().BeTrue();
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("https://m.youtube.com/@handle")).Should().BeTrue();

        // Disallowed
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("http://www.youtube.com/@handle")).Should().BeFalse(); // Non-HTTPS
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("https://attacker.com/@handle")).Should().BeFalse();
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("https://youtube.com.attacker.com")).Should().BeFalse();
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("https://127.0.0.1/@handle")).Should().BeFalse();
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("https://user:pass@youtube.com/@handle")).Should().BeFalse();
        YouTubeFeedClient.IsAllowedYouTubeHost(new Uri("https://youtube.com:8080/@handle")).Should().BeFalse();
    }

    [Test]
    public async Task GetChannelVideosAsync_ShouldFetchFeed_AndParseSuccessfully()
    {
        // Arrange
        var httpHandler = new MockHttpMessageHandler(request =>
        {
            request.RequestUri!.ToString().Should().Contain("channel_id=UC1234567890abcdefghijk");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleFeedXml)
            };
        });

        var client = new YouTubeFeedClient(new HttpClient(httpHandler), NullLogger<YouTubeFeedClient>.Instance);

        // Act
        var videos = await client.GetChannelVideosAsync("UC1234567890abcdefghijk");

        // Assert
        videos.Should().HaveCount(2);
        videos[0].VideoId.Should().Be("vid12345678");
    }

    [Test]
    public async Task GetChannelVideosAsync_ShouldReturnEmpty_WhenRequestFailsOrChannelEmpty()
    {
        // Arrange
        var httpHandler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new YouTubeFeedClient(new HttpClient(httpHandler), NullLogger<YouTubeFeedClient>.Instance);

        // Act & Assert
        (await client.GetChannelVideosAsync("")).Should().BeEmpty();
        (await client.GetChannelVideosAsync("UC_not_found")).Should().BeEmpty();
    }

    [Test]
    public async Task ResolveChannelIdAsync_ShouldResolveCanonicalId_Directly()
    {
        // Arrange
        var httpHandler = new MockHttpMessageHandler(request =>
        {
            request.RequestUri!.ToString().Should().Contain("channel_id=UC1234567890abcdefghijk");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleFeedXml)
            };
        });

        var client = new YouTubeFeedClient(new HttpClient(httpHandler), NullLogger<YouTubeFeedClient>.Instance);

        // Act
        var result = await client.ResolveChannelIdAsync("UC1234567890abcdefghijk");

        // Assert
        result.Success.Should().BeTrue();
        result.ChannelId.Should().Be("UC1234567890abcdefghijk");
        result.ChannelTitle.Should().Be("Underscore_");
    }

    [Test]
    public async Task ResolveChannelIdAsync_ShouldResolveHandle_ViaHtmlScraping()
    {
        // Arrange
        var httpHandler = new MockHttpMessageHandler(request =>
        {
            var uri = request.RequestUri!.ToString();
            if (uri.Contains("youtube.com/@Underscore_"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleHtml)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new YouTubeFeedClient(new HttpClient(httpHandler), NullLogger<YouTubeFeedClient>.Instance);

        // Act
        var result = await client.ResolveChannelIdAsync("@Underscore_");

        // Assert
        result.Success.Should().BeTrue();
        result.ChannelId.Should().Be("UC1234567890abcdefghijk");
        result.ChannelTitle.Should().Be("Underscore_");
    }

    [Test]
    public async Task ResolveChannelIdAsync_ShouldReject_DisallowedDomainsAndMalformedInput()
    {
        var client = new YouTubeFeedClient(new HttpClient(new MockHttpMessageHandler("{}")), NullLogger<YouTubeFeedClient>.Instance);

        // Empty
        (await client.ResolveChannelIdAsync("")).Success.Should().BeFalse();

        // SSRF attempt
        var ssrfResult = await client.ResolveChannelIdAsync("https://evil-site.com/@hacker");
        ssrfResult.Success.Should().BeFalse();
        ssrfResult.ErrorMessage.Should().Contain("Domaine non autorisé");

        // HTTP attempt
        var httpResult = await client.ResolveChannelIdAsync("http://youtube.com/@user");
        httpResult.Success.Should().BeFalse();
        httpResult.ErrorMessage.Should().Contain("HTTPS");

        // Redirect URL attempt
        var redirectResult = await client.ResolveChannelIdAsync("https://www.youtube.com/redirect?q=https://evil.com");
        redirectResult.Success.Should().BeFalse();
        redirectResult.ErrorMessage.Should().Contain("redirection");

        // Malformed handle with path traversal / illegal characters
        var traversalResult = await client.ResolveChannelIdAsync("@user/../../admin");
        traversalResult.Success.Should().BeFalse();
        traversalResult.ErrorMessage.Should().Contain("invalide");
    }

    [Test]
    public void ParseAtomFeed_ShouldSafelyIgnore_XXEAndMaliciousEntities()
    {
        // Arrange - XXE payload trying to read local file or trigger DTD
        var xxeXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE foo [
              <!ELEMENT foo ANY >
              <!ENTITY xxe SYSTEM "file:///etc/passwd" >]>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>&xxe;</title>
            </feed>
            """;

        // Act
        var result = YouTubeFeedClient.ParseAtomFeed(xxeXml, "UC_default");

        // Assert - DtdProcessing.Prohibit prevents parsing and returns empty safely without throwing
        result.Should().BeEmpty();
    }

    [Test]
    public async Task ResolveChannelIdAsync_ShouldBlock_RedirectToInternalOrUntrustedHost()
    {
        // Arrange - Simulates a client redirected to an internal AWS/cloud metadata IP
        var httpHandler = new MockHttpMessageHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleHtml)
            };
            // Set the final redirected RequestUri to metadata service
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://169.254.169.254/latest/meta-data/");
            return response;
        });

        var client = new YouTubeFeedClient(new HttpClient(httpHandler), NullLogger<YouTubeFeedClient>.Instance);

        // Act
        var result = await client.ResolveChannelIdAsync("@validHandle");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Redirection non autorisée");
    }

    private class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public MockHttpMessageHandler(string simpleContent) : this(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(simpleContent)
        })
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
