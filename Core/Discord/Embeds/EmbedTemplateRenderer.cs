using System.Globalization;
using System.Text.RegularExpressions;
using Discord;
using DiscordBot.DTO.Embeds;

namespace DiscordBot.Core.Discord.Embeds;

public partial class EmbedTemplateRenderer(TimeProvider timeProvider)
{
    public const int MaxTitleLength = 256;
    public const int MaxDescriptionLength = 4096;
    public const int MaxAuthorNameLength = 256;
    public const int MaxFooterLength = 2048;
    public const int MaxFieldNameLength = 256;
    public const int MaxFieldValueLength = 1024;
    public const int MaxFieldsCount = 25;
    public const int MaxTotalLength = 6000;

    [GeneratedRegex(@"\{([a-zA-Z0-9_]+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"@[\u200B-\u200D\uFEFF\u200E\u200F\s]*everyone", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EveryoneMentionRegex();

    [GeneratedRegex(@"@[\u200B-\u200D\uFEFF\u200E\u200F\s]*here", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HereMentionRegex();

    [GeneratedRegex(@"<[\u200B-\u200D\uFEFF\u200E\u200F]*@(&|!)?", RegexOptions.Compiled)]
    private static partial Regex DiscordUserOrRoleMentionRegex();

    [GeneratedRegex(@"<[\u200B-\u200D\uFEFF\u200E\u200F]*#", RegexOptions.Compiled)]
    private static partial Regex DiscordChannelMentionRegex();

    [GeneratedRegex(@"<[\u200B-\u200D\uFEFF\u200E\u200F]*/", RegexOptions.Compiled)]
    private static partial Regex DiscordSlashCommandMentionRegex();

    public Embed RenderEmbed(
        DiscordEmbedTemplateDto template,
        IReadOnlyDictionary<string, string> variables,
        string? authorIconUrl = null,
        string? authorUrl = null,
        string? embedUrl = null,
        string? imageUrl = null,
        DateTimeOffset? timestamp = null)
    {
        var color = ParseColor(template.ColorHex, GetDefaultColor(template.TemplateType));
        var builder = new EmbedBuilder().WithColor(color);

        // Title
        var rawTitle = ReplacePlaceholders(template.TitleTemplate, variables);
        var title = Truncate(rawTitle, MaxTitleLength);
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.WithTitle(title);
        }

        // Description
        var rawDescription = ReplacePlaceholders(template.DescriptionTemplate, variables);
        var description = Truncate(rawDescription, MaxDescriptionLength);
        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.WithDescription(description);
        }

        // Embed URL
        if (IsValidHttpUrl(embedUrl))
        {
            builder.WithUrl(embedUrl);
        }

        // Author
        if (template.EnableAuthor)
        {
            var rawAuthorName = ReplacePlaceholders(template.AuthorNameTemplate, variables);
            var authorName = Truncate(rawAuthorName, MaxAuthorNameLength);
            if (!string.IsNullOrWhiteSpace(authorName))
            {
                builder.WithAuthor(author =>
                {
                    author.WithName(authorName);
                    if (IsValidHttpUrl(authorUrl))
                    {
                        author.WithUrl(authorUrl);
                    }
                    if (IsValidHttpUrl(authorIconUrl))
                    {
                        author.WithIconUrl(authorIconUrl);
                    }
                });
            }
        }

        // Footer
        var rawFooter = ReplacePlaceholders(template.FooterTemplate, variables);
        var footer = Truncate(rawFooter, MaxFooterLength);
        if (!string.IsNullOrWhiteSpace(footer))
        {
            builder.WithFooter(footer);
        }

        // Timestamp
        if (template.ShowTimestamp)
        {
            builder.WithTimestamp(timestamp ?? timeProvider.GetUtcNow());
        }

        // Image / Thumbnail
        if (template.ShowThumbnail && IsValidHttpUrl(imageUrl))
        {
            builder.WithImageUrl(imageUrl);
        }

        // Custom Fields
        var fields = (template.CustomFields ?? [])
            .Where(f => f.IsEnabled)
            .OrderBy(f => f.Order)
            .Take(MaxFieldsCount)
            .ToList();

        foreach (var field in fields)
        {
            var rawFieldName = ReplacePlaceholders(field.Name, variables);
            var rawFieldValue = ReplacePlaceholders(field.Value, variables);

            var fieldName = Truncate(rawFieldName, MaxFieldNameLength);
            var fieldValue = Truncate(rawFieldValue, MaxFieldValueLength);

            if (string.IsNullOrWhiteSpace(fieldName)) fieldName = "\u200B";
            if (string.IsNullOrWhiteSpace(fieldValue)) fieldValue = "\u200B";

            builder.AddField(fieldName, fieldValue, field.Inline);
        }

        // Defensive check: total character length across all textual components <= 6000
        EnforceTotalLengthBudget(builder);

        return builder.Build();
    }

    public static string ReplacePlaceholders(string template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var dict = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);

        return PlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (dict.TryGetValue(key, out var rawVal))
            {
                return SanitizeUntrustedText(rawVal);
            }
            return match.Value;
        });
    }

    public static string SanitizeUntrustedText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var result = EveryoneMentionRegex().Replace(input, "@\u200beveryone");
        result = HereMentionRegex().Replace(result, "@\u200bhere");
        result = DiscordUserOrRoleMentionRegex().Replace(result, "<\u200b@$1");
        result = DiscordChannelMentionRegex().Replace(result, "<\u200b#");
        result = DiscordSlashCommandMentionRegex().Replace(result, "<\u200b/");

        return result;
    }

    public static Color ParseColor(string? colorHex, Color defaultColor)
    {
        if (string.IsNullOrWhiteSpace(colorHex)) return defaultColor;

        var clean = colorHex.Trim().TrimStart('#');
        if (clean.Length == 6 && uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return new Color(rgb);
        }

        return defaultColor;
    }

    public static Color GetDefaultColor(EmbedTemplateTypeDto type) => type switch
    {
        EmbedTemplateTypeDto.TwitchLive => new Color(0x91, 0x46, 0xFF),
        EmbedTemplateTypeDto.TwitchClip => new Color(0x91, 0x46, 0xFF),
        EmbedTemplateTypeDto.YouTubeVideo => new Color(0xFF, 0x00, 0x00),
        _ => new Color(0x58, 0x65, 0xF2)
    };

    public static bool IsValidHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var trimmed = text.Trim();
        if (trimmed.Length <= maxLength) return trimmed;
        if (maxLength <= 3) return trimmed[..maxLength];
        return trimmed[..(maxLength - 3)] + "...";
    }

    private static void EnforceTotalLengthBudget(EmbedBuilder builder)
    {
        var totalLength = (builder.Title?.Length ?? 0)
            + (builder.Description?.Length ?? 0)
            + (builder.Author?.Name?.Length ?? 0)
            + (builder.Footer?.Text?.Length ?? 0)
            + builder.Fields.Sum(f => (f.Name?.Length ?? 0) + (f.Value?.ToString()?.Length ?? 0));

        if (totalLength <= MaxTotalLength) return;

        // Truncate description first if needed
        var excess = totalLength - MaxTotalLength;
        if (!string.IsNullOrEmpty(builder.Description))
        {
            if (builder.Description.Length > excess + 3)
            {
                builder.Description = builder.Description[..(builder.Description.Length - excess - 3)] + "...";
                return;
            }

            excess -= builder.Description.Length;
            builder.Description = string.Empty;
        }

        // If still exceeding, drop trailing fields
        while (excess > 0 && builder.Fields.Count > 0)
        {
            var lastIndex = builder.Fields.Count - 1;
            var lastField = builder.Fields[lastIndex];
            var fieldLen = (lastField.Name?.Length ?? 0) + (lastField.Value?.ToString()?.Length ?? 0);
            builder.Fields.RemoveAt(lastIndex);
            excess -= fieldLen;
        }
    }
}
