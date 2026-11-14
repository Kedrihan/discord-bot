using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordBot.DTO;

namespace DiscordBot.Features.YouTube;

public partial class YouTubeNotificationService(
    DiscordSocketClient discordClient,
    YouTubeConfigService configService,
    YouTubeHistoryService historyService,
    YouTubeFeedClient feedClient,
    TimeProvider timeProvider,
    ILogger<YouTubeNotificationService> logger)
{
    public static readonly Color YouTubeRed = new(0xFF, 0x00, 0x00);
    private readonly SemaphoreSlim _checkLock = new(1, 1);

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

    [GeneratedRegex(@"\{(channel|title|type|url)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TemplatePlaceholderRegex();

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

    public static string SanitizeUrl(string? rawUrl, string? videoId)
    {
        if (!string.IsNullOrWhiteSpace(rawUrl) &&
            Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsedUri) &&
            parsedUri.Scheme == Uri.UriSchemeHttps &&
            YouTubeFeedClient.IsAllowedYouTubeVideoHost(parsedUri))
        {
            return parsedUri.ToString().Replace("<", "%3C").Replace(">", "%3E").Replace("@", "%40");
        }

        if (!string.IsNullOrWhiteSpace(videoId))
        {
            return $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId.Trim())}";
        }

        return string.Empty;
    }

    public static AllowedMentions BuildAllowedMentions(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return AllowedMentions.None;
        }

        var hasEveryone = template.Contains("@everyone", StringComparison.OrdinalIgnoreCase) ||
                          template.Contains("@here", StringComparison.OrdinalIgnoreCase);
        var hasRoles = template.Contains("<@&", StringComparison.OrdinalIgnoreCase);
        var hasUsers = template.Contains("<@", StringComparison.OrdinalIgnoreCase) && !hasRoles;

        if (!hasEveryone && !hasRoles && !hasUsers)
        {
            return AllowedMentions.None;
        }

        var types = AllowedMentionTypes.None;
        if (hasEveryone) types |= AllowedMentionTypes.Everyone;
        if (hasRoles) types |= AllowedMentionTypes.Roles;
        if (hasUsers) types |= AllowedMentionTypes.Users;

        return new AllowedMentions { AllowedTypes = types };
    }

    public static string FormatMessage(string? template, YouTubeVideoInfoDto video)
    {
        var cleanChannel = SanitizeUntrustedText(video.ChannelTitle);
        var cleanTitle = SanitizeUntrustedText(video.Title);
        var typeText = SanitizeUntrustedText(video.VideoType);
        var cleanUrl = SanitizeUrl(video.Url, video.VideoId);

        string formatted;
        if (string.IsNullOrWhiteSpace(template))
        {
            formatted = $"🎥 Nouvelle vidéo de **{cleanChannel}** !\n{cleanUrl}";
        }
        else
        {
            formatted = TemplatePlaceholderRegex().Replace(template, match =>
            {
                return match.Groups[1].Value.ToLowerInvariant() switch
                {
                    "channel" => cleanChannel,
                    "title" => cleanTitle,
                    "type" => typeText,
                    "url" => cleanUrl,
                    _ => match.Value
                };
            });
        }

        if (formatted.Length > 2000)
        {
            return formatted[..1997] + "...";
        }

        return formatted;
    }

    public static Embed BuildVideoEmbed(YouTubeVideoInfoDto video)
    {
        var cleanTitle = SanitizeUntrustedText(video.Title);
        var title = string.IsNullOrWhiteSpace(cleanTitle) ? "Nouvelle vidéo YouTube !" : cleanTitle.Trim();
        if (title.Length > 256)
        {
            title = title[..253] + "...";
        }

        var cleanChannel = SanitizeUntrustedText(video.ChannelTitle);
        var channelName = string.IsNullOrWhiteSpace(cleanChannel) ? "Chaîne YouTube" : cleanChannel.Trim();
        if (channelName.Length > 256)
        {
            channelName = channelName[..253] + "...";
        }

        var safeVideoUrl = SanitizeUrl(video.Url, video.VideoId);

        var embed = new EmbedBuilder()
            .WithColor(YouTubeRed)
            .WithTitle(title);

        if (!string.IsNullOrWhiteSpace(safeVideoUrl))
        {
            embed.WithUrl(safeVideoUrl);
        }

        embed.WithAuthor(author =>
        {
            author.WithName(channelName);
            if (!string.IsNullOrWhiteSpace(video.ChannelId))
            {
                var channelUrl = $"https://www.youtube.com/channel/{Uri.EscapeDataString(video.ChannelId.Trim())}";
                if (Uri.TryCreate(channelUrl, UriKind.Absolute, out var authorUri) &&
                    authorUri.Scheme == Uri.UriSchemeHttps &&
                    YouTubeFeedClient.IsAllowedYouTubeHost(authorUri))
                {
                    author.WithUrl(channelUrl);
                }
            }
        });

        embed.AddField("📺 Type", video.VideoType, inline: true)
             .AddField("📅 Publié le", $"<t:{video.PublishedAt.ToUnixTimeSeconds()}:F>", inline: true)
             .WithFooter("YouTube")
             .WithTimestamp(video.PublishedAt);

        if (!string.IsNullOrWhiteSpace(video.ThumbnailUrl) &&
            Uri.TryCreate(video.ThumbnailUrl, UriKind.Absolute, out var thumbUri) &&
            (thumbUri.Scheme == Uri.UriSchemeHttp || thumbUri.Scheme == Uri.UriSchemeHttps) &&
            YouTubeFeedClient.IsAllowedYouTubeImageHost(thumbUri))
        {
            embed.WithImageUrl(video.ThumbnailUrl);
        }

        return embed.Build();
    }

    public async Task<YouTubeExecutionResultDto> CheckNewVideosAsync(CancellationToken ct = default)
    {
        var settings = configService.GetSettings();
        if (!settings.IsEnabled)
        {
            return new YouTubeExecutionResultDto(false, "Les notifications YouTube sont désactivées dans les paramètres.");
        }

        var enabledChannels = settings.Channels.Where(c => c.IsEnabled).ToList();
        if (enabledChannels.Count == 0)
        {
            return new YouTubeExecutionResultDto(true, "Aucune chaîne YouTube active à vérifier.");
        }

        if (!await _checkLock.WaitAsync(0, ct))
        {
            return new YouTubeExecutionResultDto(false, "Une vérification des vidéos YouTube est déjà en cours.");
        }

        var processedVideosCount = 0;

        try
        {
            logger.LogInformation("Démarrage de la vérification des vidéos YouTube pour {Count} chaîne(s)...", enabledChannels.Count);

            foreach (var channel in enabledChannels)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(channel.ChannelId)) continue;

                var videos = await feedClient.GetChannelVideosAsync(channel.ChannelId, ct);
                if (videos.Count == 0) continue;

                // Cold-start protection
                if (!historyService.IsChannelInitialized(channel.ChannelId))
                {
                    await historyService.InitializeChannelAsync(channel.ChannelId, videos.Select(v => v.VideoId), ct);
                    logger.LogInformation("Protection cold-start activée : {Count} vidéo(s) marquées comme déjà vues pour la chaîne {Channel} ({ChannelId}).", videos.Count, channel.ChannelTitle, channel.ChannelId);
                    continue;
                }

                if (!ulong.TryParse(channel.DiscordChannelId, out var discordChannelId))
                {
                    logger.LogWarning("ID de salon Discord invalide pour la chaîne YouTube {Channel} : {ChannelId}", channel.ChannelTitle, channel.DiscordChannelId);
                    continue;
                }

                var messageChannel = discordClient.GetChannel(discordChannelId) as IMessageChannel;
                if (messageChannel == null)
                {
                    logger.LogWarning("Salon Discord introuvable pour la chaîne YouTube {Channel} (ID Salon : {ChannelId}).", channel.ChannelTitle, discordChannelId);
                    continue;
                }

                var newVideos = videos
                    .Where(v => !historyService.IsVideoSeen(v.VideoId))
                    .OrderBy(v => v.PublishedAt)
                    .ToList();

                var allowedMentions = BuildAllowedMentions(channel.CustomMessage);

                foreach (var video in newVideos)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var text = FormatMessage(channel.CustomMessage, video);
                        var embed = BuildVideoEmbed(video);

                        await messageChannel.SendMessageAsync(text: text, embed: embed, allowedMentions: allowedMentions);
                        await historyService.MarkVideosAsSeenAsync([video.VideoId], channel.ChannelId, ct);
                        processedVideosCount++;

                        var safeTitle = video.Title.Replace("\r", "").Replace("\n", "");
                        var safeChannel = video.ChannelTitle.Replace("\r", "").Replace("\n", "");
                        logger.LogInformation("Notification YouTube envoyée pour {Channel} : {Title} (ID: {VideoId}, Salon: {ChannelId}).", safeChannel, safeTitle, video.VideoId, discordChannelId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Erreur lors de l'envoi de la notification YouTube pour la vidéo {VideoId}.", video.VideoId);
                    }
                }
            }

            return new YouTubeExecutionResultDto(true, $"Vérification YouTube terminée ({processedVideosCount} nouvelle(s) vidéo(s) notifiée(s)).", processedVideosCount);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task<YouTubeExecutionResultDto> TestNotificationAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return new YouTubeExecutionResultDto(false, "L'identifiant de la chaîne ne peut pas être vide.");
        }

        var settings = configService.GetSettings();
        var channel = settings.Channels.FirstOrDefault(c => c.ChannelId.Equals(channelId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (channel == null)
        {
            return new YouTubeExecutionResultDto(false, $"La chaîne YouTube avec l'identifiant '{channelId}' n'a pas été trouvée dans la configuration.");
        }

        if (!ulong.TryParse(channel.DiscordChannelId, out var discordChannelId))
        {
            return new YouTubeExecutionResultDto(false, $"L'identifiant du salon Discord configuré '{channel.DiscordChannelId}' est invalide.");
        }

        var messageChannel = discordClient.GetChannel(discordChannelId) as IMessageChannel;
        if (messageChannel == null)
        {
            return new YouTubeExecutionResultDto(false, $"Impossible de trouver le salon Discord avec l'identifiant '{discordChannelId}'. Vérifiez que le bot a accès à ce salon.");
        }

        var videos = await feedClient.GetChannelVideosAsync(channel.ChannelId, ct);
        var sampleVideo = videos.FirstOrDefault() ?? new YouTubeVideoInfoDto(
            VideoId: "dQw4w9WgXcQ",
            ChannelId: channel.ChannelId,
            ChannelTitle: !string.IsNullOrWhiteSpace(channel.ChannelTitle) ? channel.ChannelTitle : "Chaîne Test",
            Title: "Vidéo de test de notification YouTube",
            Url: "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            ThumbnailUrl: "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg",
            PublishedAt: timeProvider.GetUtcNow(),
            VideoType: "Vidéo"
        );

        var text = FormatMessage(channel.CustomMessage, sampleVideo);
        var embed = BuildVideoEmbed(sampleVideo);

        await messageChannel.SendMessageAsync(text: text, embed: embed, allowedMentions: AllowedMentions.None);
        return new YouTubeExecutionResultDto(true, $"Notification de test YouTube envoyée avec succès dans le salon <#{discordChannelId}>.", 1);
    }
}
