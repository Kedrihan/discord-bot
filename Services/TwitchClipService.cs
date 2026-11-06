using System.Globalization;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TwitchClipService(
    DiscordSocketClient discordClient,
    TwitchConfigService configService,
    TwitchClipHistoryService historyService,
    TwitchApiClient apiClient,
    TimeProvider timeProvider,
    ILogger<TwitchClipService> logger)
{
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    public static string FormatClipMessage(string template, TwitchClipInfo clip)
    {
        var cleanStreamer = TwitchLiveService.SanitizeUntrustedText(clip.BroadcasterName);
        var cleanCreator = TwitchLiveService.SanitizeUntrustedText(clip.CreatorName);
        var cleanTitle = TwitchLiveService.SanitizeUntrustedText(clip.Title);
        var durationText = $"{Math.Round(clip.Duration)}s";
        var viewsText = clip.ViewCount.ToString("N0", CultureInfo.InvariantCulture);

        var cleanUrl = (Uri.TryCreate(clip.Url, UriKind.Absolute, out var parsedUri) &&
                       (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
            ? clip.Url
            : (!string.IsNullOrWhiteSpace(clip.Id) ? $"https://clips.twitch.tv/{Uri.EscapeDataString(clip.Id)}" : string.Empty);

        string formatted;
        if (string.IsNullOrWhiteSpace(template))
        {
            formatted = $"🎬 Nouveau clip de **{cleanStreamer}** créé par **{cleanCreator}** !\n{cleanUrl}";
        }
        else
        {
            formatted = template
                .Replace("{streamer}", cleanStreamer, StringComparison.OrdinalIgnoreCase)
                .Replace("{creator}", cleanCreator, StringComparison.OrdinalIgnoreCase)
                .Replace("{title}", cleanTitle, StringComparison.OrdinalIgnoreCase)
                .Replace("{duration}", durationText, StringComparison.OrdinalIgnoreCase)
                .Replace("{views}", viewsText, StringComparison.OrdinalIgnoreCase)
                .Replace("{url}", cleanUrl, StringComparison.OrdinalIgnoreCase);
        }

        if (formatted.Length > 2000)
        {
            return formatted[..1997] + "...";
        }

        return formatted;
    }

    public static Embed BuildClipEmbed(TwitchClipInfo clip)
    {
        var cleanTitle = TwitchLiveService.SanitizeUntrustedText(clip.Title);
        var title = string.IsNullOrWhiteSpace(cleanTitle) ? "Nouveau clip Twitch !" : cleanTitle.Trim();
        if (title.Length > 256)
        {
            title = title[..253] + "...";
        }

        var cleanBroadcaster = TwitchLiveService.SanitizeUntrustedText(clip.BroadcasterName);
        var broadcasterName = string.IsNullOrWhiteSpace(cleanBroadcaster) ? "Streamer" : cleanBroadcaster.Trim();
        if (broadcasterName.Length > 256)
        {
            broadcasterName = broadcasterName[..253] + "...";
        }

        var cleanCreator = TwitchLiveService.SanitizeUntrustedText(clip.CreatorName);
        var creatorName = string.IsNullOrWhiteSpace(cleanCreator) ? "Inconnu" : cleanCreator.Trim();
        if (creatorName.Length > 256)
        {
            creatorName = creatorName[..253] + "...";
        }

        var durationFormatted = $"{Math.Round(clip.Duration)}s";
        var rawClipUrl = string.IsNullOrWhiteSpace(clip.Url) ? $"https://clips.twitch.tv/{clip.Id}" : clip.Url;

        var embed = new EmbedBuilder()
            .WithColor(TwitchLiveService.TwitchPurple)
            .WithTitle(title);

        if (Uri.TryCreate(rawClipUrl, UriKind.Absolute, out var clipUri) &&
            (clipUri.Scheme == Uri.UriSchemeHttp || clipUri.Scheme == Uri.UriSchemeHttps))
        {
            embed.WithUrl(rawClipUrl);
        }

        embed.WithAuthor(author =>
        {
            author.WithName(broadcasterName);
            if (!string.IsNullOrWhiteSpace(clip.BroadcasterName))
            {
                var broadcasterUrl = $"https://twitch.tv/{Uri.EscapeDataString(clip.BroadcasterName.Trim())}";
                if (Uri.TryCreate(broadcasterUrl, UriKind.Absolute, out var authorUri) &&
                    (authorUri.Scheme == Uri.UriSchemeHttp || authorUri.Scheme == Uri.UriSchemeHttps))
                {
                    author.WithUrl(broadcasterUrl);
                }
            }
        });

        embed.AddField("🎬 Créé par", creatorName, inline: true)
            .AddField("⏱️ Durée", durationFormatted, inline: true)
            .AddField("👥 Vues", clip.ViewCount.ToString("N0", CultureInfo.InvariantCulture), inline: true)
            .AddField("📅 Date", $"<t:{clip.CreatedAt.ToUnixTimeSeconds()}:R>", inline: true)
            .WithFooter("Twitch Clips")
            .WithTimestamp(clip.CreatedAt);

        if (!string.IsNullOrWhiteSpace(clip.ThumbnailUrl) &&
            Uri.TryCreate(clip.ThumbnailUrl, UriKind.Absolute, out var thumbUri) &&
            (thumbUri.Scheme == Uri.UriSchemeHttp || thumbUri.Scheme == Uri.UriSchemeHttps))
        {
            embed.WithImageUrl(clip.ThumbnailUrl);
        }

        return embed.Build();
    }

    public async Task<TwitchClipExecutionResult> CheckNewClipsAsync(CancellationToken ct = default)
    {
        var settings = configService.GetSettings();
        if (!settings.IsEnabled)
        {
            return new TwitchClipExecutionResult(false, "Les notifications Twitch sont actuellement désactivées.");
        }

        if (!settings.IsClipsEnabled)
        {
            return new TwitchClipExecutionResult(false, "Les notifications de clips Twitch sont actuellement désactivées.");
        }

        var activeChannels = settings.Channels
            .Where(c => c.IsEnabled && c.IsClipEnabled && !string.IsNullOrWhiteSpace(c.ClipDiscordChannelId) && !string.IsNullOrWhiteSpace(c.TwitchUsername))
            .ToList();

        if (activeChannels.Count == 0)
        {
            return new TwitchClipExecutionResult(false, "Aucune chaîne Twitch active configurée pour les clips.");
        }

        if (!await _checkLock.WaitAsync(0, ct))
        {
            return new TwitchClipExecutionResult(false, "Une vérification des clips est déjà en cours d'exécution.");
        }

        try
        {
            var processedClipsCount = 0;
            var usernames = activeChannels.Select(c => c.TwitchUsername.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var users = await apiClient.GetUsersByLoginAsync(usernames, ct);
            var userMap = users.ToDictionary(u => u.Login, u => u.Id, StringComparer.OrdinalIgnoreCase);

            var startedAt = timeProvider.GetUtcNow().AddDays(-7);

            foreach (var channel in activeChannels)
            {
                var cleanUsername = channel.TwitchUsername.Trim();
                if (!userMap.TryGetValue(cleanUsername, out var broadcasterId) || string.IsNullOrEmpty(broadcasterId))
                {
                    broadcasterId = await apiClient.GetUserIdAsync(cleanUsername, ct);
                }

                if (string.IsNullOrWhiteSpace(broadcasterId))
                {
                    logger.LogWarning("Impossible de résoudre l'identifiant Twitch pour la chaîne {Username}.", cleanUsername);
                    continue;
                }

                var clips = await apiClient.GetClipsAsync(broadcasterId, startedAt: startedAt, first: 20, ct: ct);

                if (!historyService.IsBroadcasterInitialized(broadcasterId))
                {
                    await historyService.InitializeBroadcasterAsync(broadcasterId, clips.Select(c => c.Id), ct);
                    logger.LogInformation("Broadcaster {Username} ({BroadcasterId}) initialisé pour les clips. {Count} clips existants ignorés.", cleanUsername, broadcasterId, clips.Count);
                    continue;
                }

                var newClips = clips
                    .Where(c => !historyService.IsClipSeen(c.Id))
                    .OrderBy(c => c.CreatedAt)
                    .ToList();

                if (newClips.Count == 0) continue;

                if (!ulong.TryParse(channel.ClipDiscordChannelId, out var discordChannelId))
                {
                    logger.LogWarning("ID de salon Discord pour les clips invalide pour {Username}: {ChannelId}", cleanUsername, channel.ClipDiscordChannelId);
                    continue;
                }

                if (discordClient.GetChannel(discordChannelId) is not IMessageChannel messageChannel)
                {
                    logger.LogWarning("Salon Discord pour les clips introuvable ou inaccessible ({ChannelId}) pour {Username}.", discordChannelId, cleanUsername);
                    continue;
                }

                foreach (var clip in newClips)
                {
                    try
                    {
                        var text = FormatClipMessage(channel.ClipCustomMessage, clip);
                        var embed = BuildClipEmbed(clip);

                        await messageChannel.SendMessageAsync(text: text, embed: embed);
                        await historyService.MarkClipsAsSeenAsync([clip.Id], broadcasterId, ct);
                        processedClipsCount++;

                        logger.LogInformation("Notification de clip envoyée pour {Streamer}: {Title} (ID: {ClipId}, Salon: {ChannelId}).", clip.BroadcasterName, clip.Title, clip.Id, discordChannelId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Erreur lors de l'envoi de la notification du clip {ClipId} pour {Streamer}.", clip.Id, clip.BroadcasterName);
                    }
                }
            }

            return new TwitchClipExecutionResult(true, $"Vérification des clips Twitch terminée ({processedClipsCount} nouveau(x) clip(s) notifié(s)).", processedClipsCount);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task<TwitchClipExecutionResult> TestClipNotificationAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new TwitchClipExecutionResult(false, "Le nom d'utilisateur Twitch est requis.");
        }

        var settings = configService.GetSettings();
        var channel = settings.Channels.FirstOrDefault(c => string.Equals(c.TwitchUsername, username.Trim(), StringComparison.OrdinalIgnoreCase));
        if (channel == null)
        {
            return new TwitchClipExecutionResult(false, $"La chaîne '{username}' est introuvable dans la configuration.");
        }

        if (string.IsNullOrWhiteSpace(channel.ClipDiscordChannelId))
        {
            return new TwitchClipExecutionResult(false, $"Aucun salon Discord pour les clips n'est configuré pour la chaîne '{username}'.");
        }

        if (!ulong.TryParse(channel.ClipDiscordChannelId, out var discordChannelId))
        {
            return new TwitchClipExecutionResult(false, $"L'identifiant de salon Discord '{channel.ClipDiscordChannelId}' est invalide.");
        }

        if (discordClient.GetChannel(discordChannelId) is not IMessageChannel messageChannel)
        {
            return new TwitchClipExecutionResult(false, $"Salon Discord introuvable ou inaccessible ({discordChannelId}).");
        }

        var broadcasterId = await apiClient.GetUserIdAsync(channel.TwitchUsername, ct);
        TwitchClipInfo? clip = null;

        if (!string.IsNullOrEmpty(broadcasterId))
        {
            var clips = await apiClient.GetClipsAsync(broadcasterId, first: 1, ct: ct);
            if (clips.Count > 0)
            {
                clip = clips[0];
            }
        }

        clip ??= new TwitchClipInfo(
            Id: "test_clip_preview",
            Url: $"https://clips.twitch.tv/TestClip-{channel.TwitchUsername}",
            EmbedUrl: $"https://clips.twitch.tv/embed?clip=TestClip-{channel.TwitchUsername}",
            BroadcasterId: broadcasterId ?? "123456",
            BroadcasterName: channel.TwitchUsername,
            CreatorId: "78910",
            CreatorName: "ClipPreviewer",
            VideoId: "987654321",
            GameId: "509658",
            Title: $"[TEST] Clip incroyable sur {channel.TwitchUsername} !",
            ViewCount: 1337,
            CreatedAt: timeProvider.GetUtcNow(),
            ThumbnailUrl: "https://static-cdn.jtvnw.net/ttv-boxart/Just%20Chatting-285x380.jpg",
            Duration: 30.0
        );

        var text = FormatClipMessage(channel.ClipCustomMessage, clip);
        var embed = BuildClipEmbed(clip);

        await messageChannel.SendMessageAsync(text: text, embed: embed);
        return new TwitchClipExecutionResult(true, $"Notification de test de clip envoyée avec succès dans le salon <#{discordChannelId}>.", 1);
    }
}
