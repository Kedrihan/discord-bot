using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordBot.DTO.Common;
using DiscordBot.DTO.TechWatch;
using DiscordBot.Features.Twitch;

namespace DiscordBot.Features.TechWatch;

public class TechWatchService(
    DiscordSocketClient discordClient,
    TechWatchConfigService configService,
    NewsAggregatorService newsAggregator,
    GeminiSummarizerService geminiSummarizer,
    SentArticlesHistoryService historyService,
    ILogger<TechWatchService> logger)
{
    public async Task<TechWatchExecutionResultDto> ExecuteWatchAsync(bool isManualTest = false, CancellationToken cancellationToken = default)
    {
        var settings = configService.GetSettings();

        if (!settings.IsEnabled && !isManualTest)
        {
            return new TechWatchExecutionResultDto(false, "La veille technologique est désactivée.");
        }

        if (discordClient.ConnectionState != ConnectionState.Connected)
        {
            return new TechWatchExecutionResultDto(false, "Le bot Discord n'est pas connecté pour le moment.");
        }

        if (!ulong.TryParse(settings.TargetId, out var targetId) || targetId == 0)
        {
            return new TechWatchExecutionResultDto(false, "L'identifiant Discord de destination (ID Salon ou ID Utilisateur) est invalide.");
        }

        logger.LogInformation("Début de l'exécution de la veille technologique (Manuel: {IsTest})...", isManualTest);

        // 1. Récupération des articles
        var articles = await newsAggregator.FetchAllArticlesAsync(settings, filterDuplicates: !isManualTest, cancellationToken);
        if (articles.Count == 0)
        {
            return new TechWatchExecutionResultDto(false, "Aucun article récent n'a été trouvé avec les sources et thématiques actuelles.");
        }

        // 2. Synthèse et formatage
        var formattedArticles = await geminiSummarizer.SummarizeAndFormatArticlesAsync(articles, settings, cancellationToken);

        // 3. Construction du message final Discord sécurisé
        var header = isManualTest ? "🧪 **[Test] Veille Dev Web & IA**" : "🖥️ **Veille Dev Web & IA**";
        var rawMessage = $"{header}\n\n{formattedArticles}";
        var finalMessage = TwitchLiveService.SanitizeUntrustedText(rawMessage);
        if (finalMessage.Length > 2000)
        {
            finalMessage = finalMessage[..1997] + "...";
        }

        // 4. Envoi sur Discord
        try
        {
            if (settings.DestinationType == DestinationTypeDto.UserDm)
            {
                IUser? user = discordClient.GetUser(targetId);
                user ??= await discordClient.Rest.GetUserAsync(targetId);

                if (user == null)
                {
                    return new TechWatchExecutionResultDto(false, $"Impossible de trouver l'utilisateur Discord avec l'ID `{targetId}`. A-t-il un serveur en commun avec le bot ?");
                }

                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(finalMessage, allowedMentions: AllowedMentions.None);
            }
            else
            {
                var channel = discordClient.GetChannel(targetId) as IMessageChannel;
                channel ??= await discordClient.Rest.GetChannelAsync(targetId) as IMessageChannel;

                if (channel == null)
                {
                    return new TechWatchExecutionResultDto(false, $"Impossible de trouver le salon Discord avec l'ID `{targetId}`.");
                }

                await channel.SendMessageAsync(finalMessage, allowedMentions: AllowedMentions.None);
            }

            // 5. Sauvegarde des URLs envoyées pour l'anti-doublon
            if (!isManualTest)
            {
                var extractedUrls = ExtractUrls(formattedArticles);
                if (extractedUrls.Count > 0)
                {
                    await historyService.MarkAsSentAsync(extractedUrls, cancellationToken);
                    logger.LogInformation("{Count} URL(s) enregistrée(s) dans SentArticles.", extractedUrls.Count);
                }
                else
                {
                    logger.LogWarning("Aucune URL extraite du message de veille technologique ; SentArticles non mis à jour.");
                }
            }

            logger.LogInformation("Veille technologique envoyée avec succès sur Discord !");
            return new TechWatchExecutionResultDto(true, finalMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'envoi du message sur Discord");
            return new TechWatchExecutionResultDto(false, $"Erreur Discord : {ex.Message}");
        }
    }

    public static List<string> ExtractUrls(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<string> urls = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(text, @"<((?:https?:\/\/)[^>\s]+)>|(https?:\/\/[^\s<>""'\)\]]+)");

        foreach (Match match in matches)
        {
            var rawUrl = match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value.TrimEnd('.', ',', ';', ':', '!', '?');

            if (!string.IsNullOrWhiteSpace(rawUrl) && seen.Add(rawUrl))
            {
                urls.Add(rawUrl);
            }
        }

        return urls;
    }
}
