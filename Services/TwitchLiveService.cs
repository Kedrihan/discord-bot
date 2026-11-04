using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TwitchLiveService(
    DiscordSocketClient discordClient,
    TwitchConfigService configService,
    TwitchStateService stateService,
    TwitchApiClient twitchApiClient,
    ILogger<TwitchLiveService> logger)
{
    public static readonly Color TwitchPurple = new(0x91, 0x46, 0xFF);
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TwitchStreamInfo> _liveStreamsCache = new(StringComparer.OrdinalIgnoreCase);

    public event Action? OnLiveStatusChanged;

    public bool IsStreamLive(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        return _liveStreamsCache.ContainsKey(username.Trim());
    }

    public TwitchStreamInfo? GetLiveStreamInfo(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        return _liveStreamsCache.TryGetValue(username.Trim(), out var info) ? info : null;
    }

    public IReadOnlyDictionary<string, TwitchStreamInfo> GetLiveStreams() => _liveStreamsCache;

    public static string FormatMessage(string template, string streamer, string title, string game, string url)
    {
        var cleanStreamer = SanitizeUntrustedText(streamer);
        var cleanTitle = SanitizeUntrustedText(title);
        var cleanGame = SanitizeUntrustedText(game);

        if (string.IsNullOrWhiteSpace(template))
        {
            return $"🔴 **{cleanStreamer}** est en direct sur Twitch !\n{url}";
        }

        return template
            .Replace("{streamer}", cleanStreamer, StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", cleanTitle, StringComparison.OrdinalIgnoreCase)
            .Replace("{game}", cleanGame, StringComparison.OrdinalIgnoreCase)
            .Replace("{url}", url, StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizeUntrustedText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input
            .Replace("@everyone", "@\u200beveryone", StringComparison.OrdinalIgnoreCase)
            .Replace("@here", "@\u200bhere", StringComparison.OrdinalIgnoreCase)
            .Replace("<@", "<\u200b@", StringComparison.OrdinalIgnoreCase);
    }

    public static Embed BuildStreamEmbed(TwitchStreamInfo stream, string? avatarUrl)
    {
        var streamUrl = $"https://twitch.tv/{stream.UserLogin}";
        var title = string.IsNullOrWhiteSpace(stream.Title) ? "En direct sur Twitch !" : stream.Title.Trim();
        if (title.Length > 256)
        {
            title = title[..253] + "...";
        }

        var gameName = string.IsNullOrWhiteSpace(stream.GameName) ? "Non spécifiée" : stream.GameName.Trim();
        if (gameName.Length > 256)
        {
            gameName = gameName[..253] + "...";
        }

        var authorName = string.IsNullOrWhiteSpace(stream.UserName) ? stream.UserLogin : stream.UserName.Trim();
        if (authorName.Length > 256)
        {
            authorName = authorName[..253] + "...";
        }

        var embed = new EmbedBuilder()
            .WithColor(TwitchPurple)
            .WithTitle(title)
            .WithUrl(streamUrl)
            .WithAuthor(author =>
            {
                author.WithName(authorName);
                author.WithUrl(streamUrl);
                if (!string.IsNullOrWhiteSpace(avatarUrl) &&
                    Uri.TryCreate(avatarUrl, UriKind.Absolute, out var avatarUri) &&
                    (avatarUri.Scheme == Uri.UriSchemeHttp || avatarUri.Scheme == Uri.UriSchemeHttps))
                {
                    author.WithIconUrl(avatarUrl);
                }
            })
            .AddField("🎮 Jeu / Catégorie", gameName, inline: true)
            .AddField("👥 Spectateurs", stream.ViewerCount.ToString("N0"), inline: true)
            .AddField("⏱️ Début", $"<t:{stream.StartedAt.ToUnixTimeSeconds()}:R>", inline: true)
            .WithFooter("Twitch Live Notifications")
            .WithCurrentTimestamp();

        if (!string.IsNullOrWhiteSpace(stream.ThumbnailUrl))
        {
            var formattedThumbnail = stream.ThumbnailUrl
                .Replace("{width}", "1280", StringComparison.OrdinalIgnoreCase)
                .Replace("{height}", "720", StringComparison.OrdinalIgnoreCase);

            if (Uri.TryCreate(formattedThumbnail, UriKind.Absolute, out var thumbUri) &&
                (thumbUri.Scheme == Uri.UriSchemeHttp || thumbUri.Scheme == Uri.UriSchemeHttps))
            {
                embed.WithImageUrl(formattedThumbnail);
            }
        }

        return embed.Build();
    }

    public async Task<TwitchExecutionResult> CheckStreamsAsync(CancellationToken ct = default)
    {
        var settings = configService.GetSettings();
        if (!settings.IsEnabled)
        {
            return new TwitchExecutionResult(false, "Les notifications Twitch sont actuellement désactivées.");
        }

        var activeChannels = settings.Channels
            .Where(c => c.IsEnabled && !string.IsNullOrWhiteSpace(c.TwitchUsername))
            .ToList();

        if (activeChannels.Count == 0)
        {
            return new TwitchExecutionResult(false, "Aucune chaîne Twitch active à surveiller.");
        }

        if (!await _checkLock.WaitAsync(0, ct))
        {
            return new TwitchExecutionResult(false, "Une vérification des streams est déjà en cours d'exécution.");
        }

        try
        {
            var allTrackedUsernames = settings.Channels
                .Select(c => c.TwitchUsername.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            logger.LogDebug("Vérification de {Count} chaînes Twitch...", allTrackedUsernames.Count);
            var onlineStreams = await twitchApiClient.GetStreamsAsync(allTrackedUsernames, ct);
            UpdateCache(onlineStreams);

            // 1. Détecter et notifier les streams nouvellement en direct
            foreach (var stream in onlineStreams)
            {
                var channelConfig = activeChannels.FirstOrDefault(c => string.Equals(c.TwitchUsername, stream.UserLogin, StringComparison.OrdinalIgnoreCase));
                if (channelConfig == null) continue;

                var existingState = stateService.GetActiveStream(stream.UserLogin);
                if (existingState != null && existingState.StreamId == stream.StreamId)
                {
                    // Stream déjà en direct et notifié
                    continue;
                }

                if (existingState != null && existingState.StreamId != stream.StreamId)
                {
                    // Si l'état existant provient du fallback EventSub (es_...) pour la même session de stream (< 5 min d'écart), réconcilier
                    if (existingState.StreamId.StartsWith("es_") && Math.Abs((existingState.StartedAt - stream.StartedAt).TotalMinutes) < 5)
                    {
                        existingState.StreamId = stream.StreamId;
                        await stateService.SetActiveStreamAsync(existingState, ct);
                        continue;
                    }

                    // Redémarrage du stream sous un nouvel identifiant : nettoyer l'ancien message
                    await TryDeleteDiscordMessageAsync(existingState.DiscordChannelId, existingState.DiscordMessageId);
                }

                if (!ulong.TryParse(channelConfig.DiscordChannelId, out var discordChannelId))
                {
                    logger.LogWarning("ID de salon Discord invalide pour la chaîne Twitch {Username}: {ChannelId}", stream.UserLogin, channelConfig.DiscordChannelId);
                    continue;
                }

                if (discordClient.GetChannel(discordChannelId) is not IMessageChannel messageChannel)
                {
                    logger.LogWarning("Salon Discord introuvable ou inaccessible ({ChannelId}) pour la chaîne {Username}.", discordChannelId, stream.UserLogin);
                    continue;
                }

                try
                {
                    var avatarUrl = await twitchApiClient.GetUserProfileImageUrlAsync(stream.UserLogin, ct);
                    var embed = BuildStreamEmbed(stream, avatarUrl);
                    var streamUrl = $"https://twitch.tv/{stream.UserLogin}";
                    var text = FormatMessage(channelConfig.CustomMessage, stream.UserName, stream.Title, stream.GameName, streamUrl);
                    if (text.Length > 2000)
                    {
                        text = text[..1997] + "...";
                    }

                    var sentMessage = await messageChannel.SendMessageAsync(text: text, embed: embed);

                    await stateService.SetActiveStreamAsync(new TwitchActiveStreamState
                    {
                        TwitchUsername = stream.UserLogin,
                        StreamId = stream.StreamId,
                        DiscordChannelId = channelConfig.DiscordChannelId,
                        DiscordMessageId = sentMessage.Id,
                        StartedAt = stream.StartedAt
                    }, ct);

                    logger.LogInformation("Notification de stream envoyée pour {Streamer} (Salon: {ChannelId}, Message ID: {MessageId}).", stream.UserName, discordChannelId, sentMessage.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erreur lors de l'envoi de la notification de stream pour {Streamer}.", stream.UserName);
                }
            }

            // 2. Nettoyer les streams passés hors ligne
            var currentActiveStates = stateService.GetActiveStreams();
            foreach (var state in currentActiveStates)
            {
                var isStillOnline = onlineStreams.Any(s => string.Equals(s.UserLogin, state.TwitchUsername, StringComparison.OrdinalIgnoreCase));
                if (isStillOnline) continue;

                logger.LogInformation("Le streamer {Username} n'est plus en direct. Suppression du message Discord...", state.TwitchUsername);
                await TryDeleteDiscordMessageAsync(state.DiscordChannelId, state.DiscordMessageId);
                await stateService.RemoveActiveStreamAsync(state.TwitchUsername, ct);
            }

            return new TwitchExecutionResult(true, "Vérification des streams Twitch terminée.");
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task HandleStreamOnlineAsync(
        string broadcasterUserId,
        string broadcasterLogin,
        string broadcasterName,
        DateTimeOffset startedAt,
        CancellationToken ct = default)
    {
        await _checkLock.WaitAsync(ct);
        try
        {
            var settings = configService.GetSettings();
            if (!settings.IsEnabled)
            {
                logger.LogDebug("Notification de stream ignorée : notifications Twitch désactivées.");
                return;
            }

            var channelConfig = settings.Channels.FirstOrDefault(c =>
                c.IsEnabled && string.Equals(c.TwitchUsername, broadcasterLogin, StringComparison.OrdinalIgnoreCase));

            if (channelConfig == null)
            {
                logger.LogDebug("Notification ignorée pour {Streamer} : chaîne non suivie ou désactivée.", broadcasterLogin);
                return;
            }

            TwitchStreamInfo stream;
            try
            {
                var streams = await twitchApiClient.GetStreamsAsync([broadcasterLogin], ct);
                var fetchedStream = streams.FirstOrDefault();
                if (fetchedStream != null)
                {
                    stream = fetchedStream;
                }
                else
                {
                    stream = new TwitchStreamInfo
                    {
                        StreamId = $"es_{broadcasterUserId}_{startedAt.ToUnixTimeSeconds()}",
                        UserId = broadcasterUserId,
                        UserLogin = broadcasterLogin,
                        UserName = broadcasterName,
                        GameName = "En direct",
                        Title = "En direct sur Twitch !",
                        ViewerCount = 0,
                        StartedAt = startedAt,
                        ThumbnailUrl = string.Empty
                    };
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erreur lors de la récupération des détails Helix pour {Streamer}. Utilisation du fallback.", broadcasterLogin);
                stream = new TwitchStreamInfo
                {
                    StreamId = $"es_{broadcasterUserId}_{startedAt.ToUnixTimeSeconds()}",
                    UserId = broadcasterUserId,
                    UserLogin = broadcasterLogin,
                    UserName = broadcasterName,
                    GameName = "En direct",
                    Title = "En direct sur Twitch !",
                    ViewerCount = 0,
                    StartedAt = startedAt,
                    ThumbnailUrl = string.Empty
                };
            }

            var existingState = stateService.GetActiveStream(broadcasterLogin);
            if (existingState != null && (existingState.StreamId == stream.StreamId || Math.Abs((existingState.StartedAt - stream.StartedAt).TotalMinutes) < 5))
            {
                // Stream déjà en direct et notifié
                return;
            }

            if (existingState != null && existingState.StreamId != stream.StreamId)
            {
                await TryDeleteDiscordMessageAsync(existingState.DiscordChannelId, existingState.DiscordMessageId);
            }

            if (!ulong.TryParse(channelConfig.DiscordChannelId, out var discordChannelId))
            {
                logger.LogWarning("ID de salon Discord invalide pour la chaîne Twitch {Username}: {ChannelId}", broadcasterLogin, channelConfig.DiscordChannelId);
                return;
            }

            if (discordClient.GetChannel(discordChannelId) is not IMessageChannel messageChannel)
            {
                logger.LogWarning("Salon Discord introuvable ou inaccessible ({ChannelId}) pour la chaîne {Username}.", discordChannelId, broadcasterLogin);
                return;
            }

            try
            {
                var avatarUrl = await twitchApiClient.GetUserProfileImageUrlAsync(stream.UserLogin, ct);
                var embed = BuildStreamEmbed(stream, avatarUrl);
                var streamUrl = $"https://twitch.tv/{stream.UserLogin}";
                var text = FormatMessage(channelConfig.CustomMessage, stream.UserName, stream.Title, stream.GameName, streamUrl);
                if (text.Length > 2000)
                {
                    text = text[..1997] + "...";
                }

                var sentMessage = await messageChannel.SendMessageAsync(text: text, embed: embed);

                await stateService.SetActiveStreamAsync(new TwitchActiveStreamState
                {
                    TwitchUsername = stream.UserLogin,
                    StreamId = stream.StreamId,
                    DiscordChannelId = channelConfig.DiscordChannelId,
                    DiscordMessageId = sentMessage.Id,
                    StartedAt = stream.StartedAt
                }, ct);

                _liveStreamsCache[stream.UserLogin] = stream;
                NotifyLiveStatusChanged();

                logger.LogInformation("Notification de stream envoyée (EventSub) pour {Streamer} (Salon: {ChannelId}, Message ID: {MessageId}).",
                    stream.UserName, discordChannelId, sentMessage.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de l'envoi de la notification de stream pour {Streamer}.", stream.UserName);
            }
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task HandleStreamOfflineAsync(
        string broadcasterUserId,
        string broadcasterLogin,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(broadcasterLogin)) return;

        await _checkLock.WaitAsync(ct);
        try
        {
            logger.LogInformation("Le streamer {Username} n'est plus en direct (EventSub). Suppression du message Discord...", broadcasterLogin);

            var existingState = stateService.GetActiveStream(broadcasterLogin);
            if (existingState != null)
            {
                await TryDeleteDiscordMessageAsync(existingState.DiscordChannelId, existingState.DiscordMessageId);
                await stateService.RemoveActiveStreamAsync(broadcasterLogin, ct);
            }

            _liveStreamsCache.TryRemove(broadcasterLogin, out _);
            NotifyLiveStatusChanged();
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async Task<TwitchExecutionResult> TestNotificationAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return new TwitchExecutionResult(false, "Nom d'utilisateur Twitch non renseigné.");
        }

        var settings = configService.GetSettings();
        var channelConfig = settings.Channels.FirstOrDefault(c => string.Equals(c.TwitchUsername, username, StringComparison.OrdinalIgnoreCase));
        if (channelConfig == null)
        {
            return new TwitchExecutionResult(false, $"La chaîne Twitch '{username}' n'est pas dans la liste des chaînes suivies.");
        }

        if (!ulong.TryParse(channelConfig.DiscordChannelId, out var channelId))
        {
            return new TwitchExecutionResult(false, $"ID de salon Discord invalide : '{channelConfig.DiscordChannelId}'.");
        }

        if (discordClient.GetChannel(channelId) is not IMessageChannel messageChannel)
        {
            return new TwitchExecutionResult(false, $"Le salon Discord avec l'ID {channelId} est introuvable ou inaccessible par le bot.");
        }

        try
        {
            var liveStreams = await twitchApiClient.GetStreamsAsync([username], ct);
            var stream = liveStreams.FirstOrDefault();

            if (stream == null)
            {
                // Si la chaîne n'est pas en direct, on utilise des données simulées de test
                stream = new TwitchStreamInfo
                {
                    StreamId = "test_stream_123",
                    UserId = "test_user_id",
                    UserLogin = username.ToLowerInvariant(),
                    UserName = username,
                    GameName = "Just Chatting",
                    Title = $"[TEST] Stream de test pour {username}",
                    ViewerCount = 42,
                    StartedAt = DateTimeOffset.UtcNow,
                    ThumbnailUrl = "https://static-cdn.jtvnw.net/previews-ttv/live_user_test-{width}x{height}.jpg"
                };
            }

            var avatarUrl = await twitchApiClient.GetUserProfileImageUrlAsync(stream.UserLogin, ct);
            var embed = BuildStreamEmbed(stream, avatarUrl);
            var streamUrl = $"https://twitch.tv/{stream.UserLogin}";
            var text = FormatMessage(channelConfig.CustomMessage, stream.UserName, stream.Title, stream.GameName, streamUrl);
            var fullTestText = "[Notification de test]\n" + text;
            if (fullTestText.Length > 2000)
            {
                fullTestText = fullTestText[..1997] + "...";
            }

            await messageChannel.SendMessageAsync(text: fullTestText, embed: embed);

            return new TwitchExecutionResult(true, $"Notification de test envoyée avec succès dans le salon <#{channelId}>.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'envoi de la notification de test pour {Username}.", username);
            return new TwitchExecutionResult(false, $"Erreur lors de l'envoi : {ex.Message}");
        }
    }

    public async Task RefreshLiveStatusAsync(CancellationToken ct = default)
    {
        var settings = configService.GetSettings();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            return;
        }

        var allTrackedUsernames = settings.Channels
            .Select(c => c.TwitchUsername.Trim())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allTrackedUsernames.Count == 0)
        {
            if (!_liveStreamsCache.IsEmpty)
            {
                _liveStreamsCache.Clear();
                NotifyLiveStatusChanged();
            }
            return;
        }

        if (!await _checkLock.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            var onlineStreams = await twitchApiClient.GetStreamsAsync(allTrackedUsernames, ct);
            UpdateCache(onlineStreams);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erreur lors du rafraîchissement des statuts de stream Twitch.");
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private void UpdateCache(IReadOnlyList<TwitchStreamInfo> onlineStreams)
    {
        var activeLogins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stream in onlineStreams)
        {
            _liveStreamsCache[stream.UserLogin] = stream;
            activeLogins.Add(stream.UserLogin);
        }

        foreach (var key in _liveStreamsCache.Keys)
        {
            if (!activeLogins.Contains(key))
            {
                _liveStreamsCache.TryRemove(key, out _);
            }
        }

        NotifyLiveStatusChanged();
    }

    private void NotifyLiveStatusChanged()
    {
        var handler = OnLiveStatusChanged;
        if (handler == null) return;

        foreach (var receiver in handler.GetInvocationList())
        {
            try
            {
                ((Action)receiver)();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la notification OnLiveStatusChanged.");
            }
        }
    }

    private async Task TryDeleteDiscordMessageAsync(string channelIdStr, ulong messageId)
    {
        if (!ulong.TryParse(channelIdStr, out var channelId) || messageId == 0) return;

        try
        {
            if (discordClient.GetChannel(channelId) is IMessageChannel channel)
            {
                var msg = await channel.GetMessageAsync(messageId);
                if (msg != null)
                {
                    await msg.DeleteAsync();
                    logger.LogInformation("Message Discord {MessageId} supprimé avec succès.", messageId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible de supprimer le message Discord {MessageId} dans le salon {ChannelId}.", messageId, channelIdStr);
        }
    }
}
