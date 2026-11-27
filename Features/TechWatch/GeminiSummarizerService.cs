using System.Text;
using DiscordBot.Core.AI;
using DiscordBot.DTO.TechWatch;

namespace DiscordBot.Features.TechWatch;

public class GeminiSummarizerService(
    GeminiClientService geminiClient,
    ILogger<GeminiSummarizerService> logger)
{
    public async Task<string> SummarizeAndFormatArticlesAsync(
        List<NewsArticleDto> articles,
        TechWatchSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        if (articles.Count == 0)
        {
            return "Aucun nouvel article trouvé pour les sujets sélectionnés cette semaine.";
        }

        var prompt = BuildPrompt(articles, settings.MaxArticles);
        var summary = await geminiClient.GenerateContentAsync(
            prompt,
            preferredModel: settings.GeminiModel,
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            return NormalizeFormatting(summary);
        }

        if (geminiClient.IsConfigured)
        {
            logger.LogWarning("Synthèse Gemini indisponible pour la veille techno. Utilisation du formatage de secours.");
        }

        return FormatDirectFallback(articles, settings.MaxArticles);
    }

    private static string BuildPrompt(List<NewsArticleDto> articles, int maxArticles)
    {
        var articlesListText = new StringBuilder();
        foreach (var a in articles)
        {
            articlesListText.AppendLine($"• {a.Title} → <{a.Url}>");
        }

        return $"""
Tu es un assistant expert de veille technologique pour développeur.
Voici une liste d'articles récents :
{articlesListText}

Consignes :
1. Sélectionne les {maxArticles} articles les plus pertinents, variés et intéressants, UNIQUEMENT en français ou en anglais (ignore strictement toute autre langue).
2. Pour chaque article sélectionné, utilise STRICTEMENT ce format :
• **Titre traduit en français si nécessaire** (15 mots MAX)
  <url_exacte_copiée_tel_quel>
3. Le lien doit obligatoirement être sur une nouvelle ligne avec 2 espaces d'indentation, et encadré de chevrons < et >.
4. AUCUNE phrase d'introduction, AUCUNE conclusion, AUCUNE catégorie, AUCUN blabla. Uniquement la liste des bullet points.
""";
    }

    private static string NormalizeFormatting(string text) => text
        .Replace("*   **", "• **")
        .Replace("*  **", "• **")
        .Replace("* **", "• **")
        .Replace("- **", "• **")
        .Trim();

    private static string FormatDirectFallback(List<NewsArticleDto> articles, int maxArticles)
    {
        var sb = new StringBuilder();
        foreach (var a in articles.Take(maxArticles))
        {
            sb.AppendLine($"• **{a.Title}**");
            sb.AppendLine($"  <{a.Url}>");
        }
        return sb.ToString().TrimEnd();
    }
}
