using System.Text;
using DiscordBot.DTO;
using Google.GenAI;
using Google.GenAI.Types;

namespace DiscordBot.Services;

public class GeminiSummarizerService(ILogger<GeminiSummarizerService> logger)
{
    public async Task<string> SummarizeAndFormatArticlesAsync(List<NewsArticleDto> articles, TechWatchSettingsDto settings)
    {
        if (articles.Count == 0)
        {
            return "Aucun nouvel article trouvé pour les sujets sélectionnés cette semaine.";
        }

        // Si la clé Gemini est configurée, on utilise l'IA via le SDK officiel Google.GenAI
        if (!string.IsNullOrWhiteSpace(settings.GeminiApiKey))
        {
            try
            {
                var summary = await CallGeminiAsync(articles, settings);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    return summary;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de l'appel au SDK Google Gen AI. Utilisation du formatage de secours.");
            }
        }

        // Formatage direct de secours (sans IA ou en cas d'erreur API)
        return FormatDirectFallback(articles, settings.MaxArticles);
    }

    private async Task<string> CallGeminiAsync(List<NewsArticleDto> articles, TechWatchSettingsDto settings)
    {
        var model = string.IsNullOrWhiteSpace(settings.GeminiModel) || settings.GeminiModel.StartsWith("gemini-1.") || settings.GeminiModel == "gemini-2.0-flash"
            ? "gemini-flash-latest"
            : settings.GeminiModel;

        var articlesListText = new StringBuilder();
        foreach (var a in articles)
        {
            articlesListText.AppendLine($"• {a.Title} → <{a.Url}>");
        }

        var prompt = $"""
Tu es un assistant expert de veille technologique pour développeur.
Voici une liste d'articles récents :
{articlesListText}

Consignes :
1. Sélectionne les {settings.MaxArticles} articles les plus pertinents, variés et intéressants.
2. Pour chaque article sélectionné, utilise STRICTEMENT ce format :
• **Titre traduit en français si nécessaire** (15 mots MAX)
  <url_exacte_copiée_tel_quel>
3. Le lien doit obligatoirement être sur une nouvelle ligne avec 2 espaces d'indentation, et encadré de chevrons < et >.
4. AUCUNE phrase d'introduction, AUCUNE conclusion, AUCUNE catégorie, AUCUN blabla. Uniquement la liste des bullet points.
""";

        var client = new Client(apiKey: settings.GeminiApiKey);

        GenerateContentResponse? response = null;
        try
        {
            response = await client.Models.GenerateContentAsync(
                model: model,
                contents: prompt
            );
        }
        catch (Exception ex) when (model != "gemini-flash-latest")
        {
            logger.LogWarning(ex, "Le modèle {Model} a échoué. Repli automatique avec le SDK sur gemini-flash-latest...", model);
            response = await client.Models.GenerateContentAsync(
                model: "gemini-flash-latest",
                contents: prompt
            );
        }

        var text = response?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Nettoyage et uniformisation du formatage (comme dans Home Assistant)
        return text
            .Replace("*   **", "• **")
            .Replace("*  **", "• **")
            .Replace("* **", "• **")
            .Replace("- **", "• **")
            .Trim();
    }

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
