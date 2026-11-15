using Discord;
using DiscordBot.Core.Discord.Embeds;
using DiscordBot.DTO.Embeds;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Core.Discord.Embeds;

[TestFixture]
public class TUEmbedTemplateRendererTests
{
    private FakeTimeProvider _timeProvider = null!;
    private EmbedTemplateRenderer _renderer = null!;

    [SetUp]
    public void SetUp()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
        _renderer = new EmbedTemplateRenderer(_timeProvider);
    }

    [Test]
    public void RenderEmbed_WithDefaultTwitchLiveTemplate_ShouldMatchExpectedStructure()
    {
        // Arrange
        var template = new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.TwitchLive,
            ColorHex = "#9146FF",
            TitleTemplate = "{StreamTitle}",
            DescriptionTemplate = "",
            EnableAuthor = true,
            AuthorNameTemplate = "{StreamerName}",
            FooterTemplate = "Twitch Live Notifications",
            ShowThumbnail = true,
            ShowTimestamp = true,
            CustomFields =
            [
                new("🎮 Jeu / Catégorie", "{GameName}", Inline: true, IsEnabled: true, Order: 0),
                new("👥 Spectateurs", "{ViewerCount}", Inline: true, IsEnabled: true, Order: 1),
                new("⏱️ Début", "<t:{StartedAtUnix}:R>", Inline: true, IsEnabled: true, Order: 2)
            ]
        };

        var variables = new Dictionary<string, string>
        {
            ["StreamTitle"] = "Session C# .NET 10",
            ["StreamerName"] = "AlexDev",
            ["GameName"] = "Software Development",
            ["ViewerCount"] = "1,500",
            ["StartedAtUnix"] = "1731664800"
        };

        // Act
        var embed = _renderer.RenderEmbed(
            template,
            variables,
            authorIconUrl: "https://example.com/avatar.png",
            authorUrl: "https://twitch.tv/alexdev",
            embedUrl: "https://twitch.tv/alexdev",
            imageUrl: "https://example.com/stream.jpg");

        // Assert
        embed.Title.Should().Be("Session C# .NET 10");
        embed.Color.Should().Be(new Color(0x91, 0x46, 0xFF));
        embed.Author.Should().NotBeNull();
        embed.Author!.Value.Name.Should().Be("AlexDev");
        embed.Author!.Value.Url.Should().Be("https://twitch.tv/alexdev");
        embed.Author!.Value.IconUrl.Should().Be("https://example.com/avatar.png");
        embed.Url.Should().Be("https://twitch.tv/alexdev");
        embed.Image.Should().NotBeNull();
        embed.Image!.Value.Url.Should().Be("https://example.com/stream.jpg");
        embed.Footer.Should().NotBeNull();
        embed.Footer!.Value.Text.Should().Be("Twitch Live Notifications");
        embed.Timestamp.Should().Be(_timeProvider.GetUtcNow());

        embed.Fields.Should().HaveCount(3);
        embed.Fields[0].Name.Should().Be("🎮 Jeu / Catégorie");
        embed.Fields[0].Value.Should().Be("Software Development");
        embed.Fields[0].Inline.Should().BeTrue();
        embed.Fields[1].Name.Should().Be("👥 Spectateurs");
        embed.Fields[1].Value.Should().Be("1,500");
        embed.Fields[2].Name.Should().Be("⏱️ Début");
        embed.Fields[2].Value.Should().Be("<t:1731664800:R>");
    }

    [Test]
    public void RenderEmbed_WithDefaultYouTubeVideoTemplate_ShouldMatchExpectedStructure()
    {
        // Arrange
        var template = new DiscordEmbedTemplateDto
        {
            TemplateType = EmbedTemplateTypeDto.YouTubeVideo,
            ColorHex = "#FF0000",
            TitleTemplate = "{VideoTitle}",
            DescriptionTemplate = "Nouvelle sortie vidéo",
            EnableAuthor = true,
            AuthorNameTemplate = "{ChannelName}",
            FooterTemplate = "YouTube",
            ShowThumbnail = true,
            ShowTimestamp = true,
            CustomFields =
            [
                new("📺 Type", "{VideoType}", Inline: true, IsEnabled: true, Order: 0),
                new("📅 Publié le", "<t:{PublishedAtUnix}:F>", Inline: true, IsEnabled: true, Order: 1)
            ]
        };

        var variables = new Dictionary<string, string>
        {
            ["VideoTitle"] = "Vidéo .NET 10",
            ["ChannelName"] = "Tech Channel",
            ["VideoType"] = "Vidéo",
            ["PublishedAtUnix"] = "1731664800"
        };

        // Act
        var embed = _renderer.RenderEmbed(
            template,
            variables,
            authorUrl: "https://youtube.com/channel/123",
            embedUrl: "https://youtube.com/watch?v=123",
            imageUrl: "https://example.com/thumb.jpg");

        // Assert
        embed.Title.Should().Be("Vidéo .NET 10");
        embed.Description.Should().Be("Nouvelle sortie vidéo");
        embed.Color.Should().Be(new Color(0xFF, 0x00, 0x00));
        embed.Author!.Value.Name.Should().Be("Tech Channel");
        embed.Fields.Should().HaveCount(2);
        embed.Fields[0].Name.Should().Be("📺 Type");
        embed.Fields[0].Value.Should().Be("Vidéo");
    }

    [Test]
    public void RenderEmbed_ShouldDefensivelyTruncate_WhenLengthsExceedDiscordLimits()
    {
        // Arrange
        var longTitle = new string('A', 300);
        var longDesc = new string('B', 5000);
        var longAuthor = new string('C', 300);
        var longFooter = new string('D', 2500);
        var longFieldName = new string('E', 300);
        var longFieldValue = new string('F', 1500);

        var template = new DiscordEmbedTemplateDto
        {
            ColorHex = "#9146FF",
            TitleTemplate = "{Title}",
            DescriptionTemplate = "{Desc}",
            EnableAuthor = true,
            AuthorNameTemplate = "{Author}",
            FooterTemplate = "{Footer}",
            ShowThumbnail = false,
            ShowTimestamp = false,
            CustomFields =
            [
                new(longFieldName, longFieldValue, Inline: true, IsEnabled: true, Order: 0)
            ]
        };

        var variables = new Dictionary<string, string>
        {
            ["Title"] = longTitle,
            ["Desc"] = longDesc,
            ["Author"] = longAuthor,
            ["Footer"] = longFooter
        };

        // Act
        var embed = _renderer.RenderEmbed(template, variables);

        // Assert
        embed.Title!.Length.Should().BeLessOrEqualTo(EmbedTemplateRenderer.MaxTitleLength);
        embed.Title.Should().EndWith("...");

        embed.Description!.Length.Should().BeLessOrEqualTo(EmbedTemplateRenderer.MaxDescriptionLength);
        embed.Description.Should().EndWith("...");

        embed.Author!.Value.Name.Length.Should().BeLessOrEqualTo(EmbedTemplateRenderer.MaxAuthorNameLength);
        embed.Author!.Value.Name.Should().EndWith("...");

        embed.Footer!.Value.Text.Length.Should().BeLessOrEqualTo(EmbedTemplateRenderer.MaxFooterLength);
        embed.Footer!.Value.Text.Should().EndWith("...");

        embed.Fields[0].Name.Length.Should().BeLessOrEqualTo(EmbedTemplateRenderer.MaxFieldNameLength);
        embed.Fields[0].Name.Should().EndWith("...");

        embed.Fields[0].Value.Length.Should().BeLessOrEqualTo(EmbedTemplateRenderer.MaxFieldValueLength);
        embed.Fields[0].Value.Should().EndWith("...");
    }

    [Test]
    public void RenderEmbed_ShouldCapFields_AtMax25()
    {
        // Arrange
        var fields = Enumerable.Range(0, 30)
            .Select(i => new DiscordEmbedFieldTemplateDto($"Field {i}", $"Val {i}", Inline: true, IsEnabled: true, Order: i))
            .ToList();

        var template = new DiscordEmbedTemplateDto
        {
            TitleTemplate = "Max Fields Test",
            CustomFields = fields
        };

        // Act
        var embed = _renderer.RenderEmbed(template, new Dictionary<string, string>());

        // Assert
        embed.Fields.Should().HaveCount(25);
        embed.Fields.Last().Name.Should().Be("Field 24");
    }

    [Test]
    public void ReplacePlaceholders_ShouldSanitizeMentionsInDynamicVariables()
    {
        // Arrange
        var template = "Titre: {Title} par {Streamer}";
        var variables = new Dictionary<string, string>
        {
            ["Title"] = "Passez voir @everyone et @here !",
            ["Streamer"] = "<@&123456789> et <#987654321>"
        };

        // Act
        var result = EmbedTemplateRenderer.ReplacePlaceholders(template, variables);

        // Assert
        result.Should().NotContain("@everyone");
        result.Should().Contain("@\u200beveryone");
        result.Should().NotContain("@here");
        result.Should().Contain("@\u200bhere");
        result.Should().NotContain("<@&");
        result.Should().Contain("<\u200b@&123456789>");
        result.Should().NotContain("<#");
        result.Should().Contain("<\u200b#987654321>");
    }

    [Test]
    public void ReplacePlaceholders_ShouldBeCaseInsensitive()
    {
        // Arrange
        var template = "Live: {streamtitle} par {STREAMERNAME}";
        var variables = new Dictionary<string, string>
        {
            ["StreamTitle"] = "Super Live",
            ["StreamerName"] = "Alex"
        };

        // Act
        var result = EmbedTemplateRenderer.ReplacePlaceholders(template, variables);

        // Assert
        result.Should().Be("Live: Super Live par Alex");
    }

    [TestCase("#9146FF", 0x91, 0x46, 0xFF)]
    [TestCase("FF0000", 0xFF, 0x00, 0x00)]
    [TestCase("#5865F2", 0x58, 0x65, 0xF2)]
    public void ParseColor_ShouldParseValidHex(string hex, byte r, byte g, byte b)
    {
        var defaultColor = new Color(0, 0, 0);
        var parsed = EmbedTemplateRenderer.ParseColor(hex, defaultColor);

        parsed.R.Should().Be(r);
        parsed.G.Should().Be(g);
        parsed.B.Should().Be(b);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("invalid-hex")]
    [TestCase("#12345")]
    public void ParseColor_ShouldFallbackOnInvalidHex(string hex)
    {
        var defaultColor = new Color(0x91, 0x46, 0xFF);
        var parsed = EmbedTemplateRenderer.ParseColor(hex, defaultColor);

        parsed.Should().Be(defaultColor);
    }

    [TestCase("https://twitch.tv/test", true)]
    [TestCase("http://example.com/test", true)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("file:///etc/passwd", false)]
    [TestCase("ftp://example.com", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsValidHttpUrl_ShouldValidateCorrectly(string? url, bool expected)
    {
        EmbedTemplateRenderer.IsValidHttpUrl(url).Should().Be(expected);
    }

    [Test]
    public void RenderEmbed_ShouldFilterDisabledFields_AndHonorOrder()
    {
        // Arrange
        var template = new DiscordEmbedTemplateDto
        {
            TitleTemplate = "Ordering Test",
            CustomFields =
            [
                new("Field 2", "Val 2", Inline: true, IsEnabled: true, Order: 2),
                new("Field 0", "Val 0", Inline: true, IsEnabled: true, Order: 0),
                new("Field Disabled", "Val Disabled", Inline: true, IsEnabled: false, Order: 1),
                new("Field 1", "Val 1", Inline: true, IsEnabled: true, Order: 1)
            ]
        };

        // Act
        var embed = _renderer.RenderEmbed(template, new Dictionary<string, string>());

        // Assert
        embed.Fields.Should().HaveCount(3);
        embed.Fields[0].Name.Should().Be("Field 0");
        embed.Fields[1].Name.Should().Be("Field 1");
        embed.Fields[2].Name.Should().Be("Field 2");
    }

    [Test]
    public void SanitizeUntrustedText_ShouldNeutralizeZeroWidthAndRoleMentions()
    {
        // Arrange
        var zeroWidthEveryone = "@\u200beveryone";
        var zeroWidthRole = "<\u200b@\u200b&123456789>";
        var slashCommand = "</play:123456>";

        // Act
        var resEveryone = EmbedTemplateRenderer.SanitizeUntrustedText(zeroWidthEveryone);
        var resRole = EmbedTemplateRenderer.SanitizeUntrustedText(zeroWidthRole);
        var resSlash = EmbedTemplateRenderer.SanitizeUntrustedText(slashCommand);

        // Assert
        resEveryone.Should().Be("@\u200beveryone");
        resRole.Should().Contain("<\u200b@");
        resSlash.Should().Be("<\u200b/play:123456>");
    }

    [TestCase("javascript:alert(1)")]
    [TestCase("JAVASCRIPT:void(0)")]
    [TestCase("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [TestCase("vbscript:msgbox(1)")]
    [TestCase("file:///C:/Windows/System32/calc.exe")]
    public void IsValidHttpUrl_ShouldRejectDangerousSchemes(string dangerousUrl)
    {
        EmbedTemplateRenderer.IsValidHttpUrl(dangerousUrl).Should().BeFalse();
    }

    [TestCase("red; position: fixed; top: 0;")]
    [TestCase("#9146FF; background: url('evil.com')")]
    [TestCase("<script>alert(1)</script>")]
    public void ParseColor_ShouldRejectCssInjectionPayloads(string maliciousColor)
    {
        var defaultColor = new Color(0x91, 0x46, 0xFF);
        var parsed = EmbedTemplateRenderer.ParseColor(maliciousColor, defaultColor);

        parsed.Should().Be(defaultColor);
        var safeHex = $"#{parsed.RawValue:X6}";
        safeHex.Should().Be("#9146FF");
    }

    [Test]
    public void ReplacePlaceholders_ShouldHandleLargeInputs_WithoutReDoS()
    {
        // Arrange
        var largeInput = new string('@', 10000) + "everyone";
        var variables = new Dictionary<string, string>
        {
            ["test"] = largeInput
        };

        // Act
        var act = () => EmbedTemplateRenderer.ReplacePlaceholders("{test}", variables);

        // Assert
        act.Should().NotThrow();
    }
}
