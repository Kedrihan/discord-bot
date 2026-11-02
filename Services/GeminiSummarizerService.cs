using System.Text;
using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class GeminiSummarizerService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiSummarizerService> _logger;

    public GeminiSummarizerService(HttpClient httpClient, ILogger<GeminiSummarizerService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> SummarizeAndFormatArticlesAsync(List<NewsArticle> articles, TechWatchSettings settings)
    {
        if (articles.Count == 0)
        {
            return "Aucun nouvel article trouvé pour les sujets sélectionnés cette semaine.";
        }

        // Si la clé Gemini est configurée, on utilise l'IA
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
                _logger.LogError(ex, "Erreur lors de l'appel à l'API Gemini. Utilisation du formattage de secours.");
            }
        }

        // Formattage direct de secours (sans IA ou en cas d'erreur API)
        return FormatDirectFallback(articles, settings.MaxArticles);
    }

    private async Task<string> CallGeminiAsync(List<NewsArticle> articles, TechWatchSettings settings)
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

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={settings.GeminiApiKey}";
        var response = await _httpClient.PostAsync(url, content);

        // Si le modèle configuré échoue (ex: 404 déprécié ou 503 temporaire), repli automatique sur gemini-flash-latest
        if (!response.IsSuccessStatusCode && model != "gemini-flash-latest")
        {
            _logger.LogWarning("Le modèle {Model} a échoué ({Status}). Repli automatique sur gemini-flash-latest...", model, response.StatusCode);
            url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent?key={settings.GeminiApiKey}";
            content = new StringContent(json, Encoding.UTF8, "application/json");
            response = await _httpClient.PostAsync(url, content);
        }

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini API a retourné le code {Status}: {Error}", response.StatusCode, err);
            return string.Empty;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

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

    private string FormatDirectFallback(List<NewsArticle> articles, int maxArticles)
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
