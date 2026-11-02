using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TechWatchService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly TechWatchConfigService _configService;
    private readonly NewsAggregatorService _newsAggregator;
    private readonly GeminiSummarizerService _geminiSummarizer;
    private readonly SentArticlesHistoryService _historyService;
    private readonly ILogger<TechWatchService> _logger;

    public TechWatchService(
        DiscordSocketClient discordClient,
        TechWatchConfigService configService,
        NewsAggregatorService newsAggregator,
        GeminiSummarizerService geminiSummarizer,
        SentArticlesHistoryService historyService,
        ILogger<TechWatchService> logger)
    {
        _discordClient = discordClient;
        _configService = configService;
        _newsAggregator = newsAggregator;
        _geminiSummarizer = geminiSummarizer;
        _historyService = historyService;
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> ExecuteWatchAsync(bool isManualTest = false)
    {
        var settings = _configService.GetSettings();

        if (!settings.IsEnabled && !isManualTest)
        {
            return (false, "La veille technologique est désactivée.");
        }

        if (_discordClient.ConnectionState != ConnectionState.Connected)
        {
            return (false, "Le bot Discord n'est pas connecté pour le moment.");
        }

        if (!ulong.TryParse(settings.TargetId, out var targetId) || targetId == 0)
        {
            return (false, "L'identifiant Discord de destination (ID Salon ou ID Utilisateur) est invalide.");
        }

        _logger.LogInformation("Début de l'exécution de la veille technologique (Manuel: {IsTest})...", isManualTest);

        // 1. Récupération des articles
        var articles = await _newsAggregator.FetchAllArticlesAsync(settings, filterDuplicates: !isManualTest);
        if (articles.Count == 0)
        {
            return (false, "Aucun article récent n'a été trouvé avec les sources et thématiques actuelles.");
        }

        // 2. Synthèse et formatage
        var formattedArticles = await _geminiSummarizer.SummarizeAndFormatArticlesAsync(articles, settings);

        // 3. Construction du message final Discord
        var header = isManualTest ? "🧪 **[Test] Veille Dev Web & IA**" : "🖥️ **Veille Dev Web & IA**";
        var finalMessage = $"{header}\n\n{formattedArticles}";

        // 4. Envoi sur Discord
        try
        {
            if (settings.DestinationType == DestinationType.UserDm)
            {
                var user = _discordClient.GetUser(targetId) 
                    ?? await _discordClient.Rest.GetUserAsync(targetId);

                if (user == null)
                {
                    return (false, $"Impossible de trouver l'utilisateur Discord avec l'ID `{targetId}`. A-t-il un serveur en commun avec le bot ?");
                }

                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(finalMessage);
            }
            else
            {
                var channel = _discordClient.GetChannel(targetId) as IMessageChannel
                    ?? await _discordClient.Rest.GetChannelAsync(targetId) as IMessageChannel;

                if (channel == null)
                {
                    return (false, $"Impossible de trouver le salon Discord avec l'ID `{targetId}`.");
                }

                await channel.SendMessageAsync(finalMessage);
            }

            // 5. Sauvegarde des URLs envoyées pour l'anti-doublon
            if (!isManualTest)
            {
                var extractedUrls = ExtractUrls(formattedArticles);
                if (extractedUrls.Count > 0)
                {
                    await _historyService.MarkAsSentAsync(extractedUrls);
                }
            }

            _logger.LogInformation("Veille technologique envoyée avec succès sur Discord !");
            return (true, finalMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'envoi du message sur Discord");
            return (false, $"Erreur Discord : {ex.Message}");
        }
    }

    private List<string> ExtractUrls(string text)
    {
        var urls = new List<string>();
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
