using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordBot.DTO;

namespace DiscordBot.Services;

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
        var formattedArticles = await geminiSummarizer.SummarizeAndFormatArticlesAsync(articles, settings);

        // 3. Construction du message final Discord
        var header = isManualTest ? "🧪 **[Test] Veille Dev Web & IA**" : "🖥️ **Veille Dev Web & IA**";
        var finalMessage = $"{header}\n\n{formattedArticles}";

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
                await dm.SendMessageAsync(finalMessage);
            }
            else
            {
                var channel = discordClient.GetChannel(targetId) as IMessageChannel;
                channel ??= await discordClient.Rest.GetChannelAsync(targetId) as IMessageChannel;

                if (channel == null)
                {
                    return new TechWatchExecutionResultDto(false, $"Impossible de trouver le salon Discord avec l'ID `{targetId}`.");
                }

                await channel.SendMessageAsync(finalMessage);
            }

            // 5. Sauvegarde des URLs envoyées pour l'anti-doublon
            if (!isManualTest)
            {
                var extractedUrls = ExtractUrls(formattedArticles);
                if (extractedUrls.Count > 0)
                {
                    await historyService.MarkAsSentAsync(extractedUrls, cancellationToken);
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
        List<string> urls = [];
        var matches = Regex.Matches(text, @"<((?:https?:\/\/)[^>]+)>");
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                urls.Add(match.Groups[1].Value);
            }
        }
        return urls;
    }
}
