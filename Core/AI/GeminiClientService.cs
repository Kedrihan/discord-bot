using Google.GenAI;
using Google.GenAI.Types;

namespace DiscordBot.Core.AI;

public class GeminiClientService(
    ILogger<GeminiClientService> logger,
    TimeProvider timeProvider,
    IConfiguration? configuration = null)
{
    private const string DefaultPrimaryModel = "gemini-2.5-flash";

    private static readonly string[] FallbackModels =
    [
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite",
        "gemini-flash-latest",
        "gemini-flash-lite-latest"
    ];

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15)
    ];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());

    public virtual async Task<string?> GenerateContentAsync(
        string prompt,
        string? preferredModel = null,
        string? responseMimeType = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var modelsToTry = BuildModelFallbackChain(preferredModel);
        var maxAttemptsPerModel = RetryDelays.Length + 1;

        foreach (var model in modelsToTry)
        {
            for (var attempt = 1; attempt <= maxAttemptsPerModel; attempt++)
            {
                try
                {
                    var text = await InvokeModelAsync(model, prompt, apiKey, responseMimeType, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        logger.LogInformation(
                            "Réponse Gemini reçue avec succès via {Model} (tentative {Attempt}/{MaxAttempts}).",
                            model,
                            attempt,
                            maxAttemptsPerModel);
                        return text;
                    }

                    logger.LogWarning("Réponse vide reçue du modèle Gemini {Model}. Passage au modèle suivant.", model);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsAuthenticationError(ex))
                {
                    logger.LogError(ex, "Erreur d'authentification API Gemini ({Model}). Vérifiez GEMINI_API_KEY.", model);
                    return null;
                }
                catch (Exception ex) when (IsTransientError(ex, cancellationToken) && attempt < maxAttemptsPerModel)
                {
                    var delay = RetryDelays[attempt - 1];
                    logger.LogWarning(
                        ex,
                        "Erreur transitoire Gemini sur {Model} (tentative {Attempt}/{MaxAttempts}). Nouvelle tentative dans {DelaySeconds}s...",
                        model,
                        attempt,
                        maxAttemptsPerModel,
                        delay.TotalSeconds);

                    await Task.Delay(delay, timeProvider, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Échec du modèle Gemini {Model} (tentative {Attempt}/{MaxAttempts}). Bascule vers le modèle de secours suivant...",
                        model,
                        attempt,
                        maxAttemptsPerModel);
                    break;
                }
            }
        }

        logger.LogError("Tous les modèles Gemini de la cascade ont échoué.");
        return null;
    }

    public static IReadOnlyList<string> BuildModelFallbackChain(string? configuredModel)
    {
        var primaryModel = string.IsNullOrWhiteSpace(configuredModel)
            || configuredModel.StartsWith("gemini-1.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredModel, "gemini-2.0-flash", StringComparison.OrdinalIgnoreCase)
            ? DefaultPrimaryModel
            : configuredModel.Trim();

        List<string> chain = [primaryModel];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase) { primaryModel };

        foreach (var fallback in FallbackModels)
        {
            if (seen.Add(fallback))
            {
                chain.Add(fallback);
            }
        }

        return chain;
    }

    internal static bool IsTransientError(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        ServerError => true,
        ClientError clientError => clientError.StatusCode == 429,
        HttpRequestException => true,
        TaskCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false
    };

    internal static bool IsAuthenticationError(Exception ex) =>
        ex is ClientError { StatusCode: 401 or 403 };

    protected virtual async Task<string?> InvokeModelAsync(
        string model,
        string prompt,
        string apiKey,
        string? responseMimeType,
        CancellationToken cancellationToken)
    {
        var client = new Client(apiKey: apiKey);
        GenerateContentConfig? config = string.IsNullOrWhiteSpace(responseMimeType)
            ? null
            : new GenerateContentConfig { ResponseMimeType = responseMimeType };

        var response = await client.Models.GenerateContentAsync(
            model: model,
            contents: prompt,
            config: config,
            cancellationToken: cancellationToken);

        return response?.Text;
    }

    private string? ResolveApiKey() =>
        configuration?["GEMINI_API_KEY"] ?? System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");
}
