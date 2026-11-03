using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class MorningBriefingService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly MorningBriefingConfigService _configService;
    private readonly TechWatchConfigService _techWatchConfigService;
    private readonly OpenMeteoService _weatherService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MorningBriefingService> _logger;

    public MorningBriefingService(
        DiscordSocketClient discordClient,
        MorningBriefingConfigService configService,
        TechWatchConfigService techWatchConfigService,
        OpenMeteoService weatherService,
        HttpClient httpClient,
        ILogger<MorningBriefingService> logger)
    {
        _discordClient = discordClient;
        _configService = configService;
        _techWatchConfigService = techWatchConfigService;
        _weatherService = weatherService;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(bool Success, string Message, Embed? Embed)> GenerateBriefingEmbedAsync(bool isManualTest = false)
    {
        var settings = _configService.GetSettings();
        var commute = await _weatherService.GetCommuteWeatherAsync(settings);
        if (commute == null)
        {
            return (false, "Impossible de récupérer les prévisions météo Open-Meteo.", null);
        }

        var briefingResult = await AnalyzeWithGeminiAsync(commute.Value.Aller, commute.Value.Retour, settings);
        var embed = BuildDiscordEmbed(briefingResult, commute.Value.Aller, commute.Value.Retour, settings, isManualTest);
        return (true, "Briefing généré avec succès.", embed);
    }

    public async Task<(bool Success, string Message, Embed? Embed)> ExecuteBriefingAsync(bool isManualTest = false)
    {
        var settings = _configService.GetSettings();
        if (!settings.IsEnabled && !isManualTest)
        {
            return (false, "Le Morning Briefing est désactivé.", null);
        }

        if (_discordClient.ConnectionState != ConnectionState.Connected)
        {
            return (false, "Le bot Discord n'est pas connecté.", null);
        }

        if (!ulong.TryParse(settings.TargetId, out var targetId) || targetId == 0)
        {
            return (false, "L'identifiant Discord cible est invalide.", null);
        }

        _logger.LogInformation("Génération du Morning Briefing Vélo (Test: {IsTest})...", isManualTest);

        var (success, genMessage, embed) = await GenerateBriefingEmbedAsync(isManualTest);
        if (!success || embed == null)
        {
            return (false, genMessage, null);
        }

        // Envoi Discord
        try
        {
            if (settings.DestinationType == DestinationType.UserDm)
            {
                IUser? user = _discordClient.GetUser(targetId);
                if (user == null)
                {
                    user = await _discordClient.Rest.GetUserAsync(targetId);
                }

                if (user == null)
                {
                    return (false, $"Utilisateur Discord `{targetId}` introuvable.", null);
                }

                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(embed: embed);
            }
            else
            {
                IMessageChannel? channel = _discordClient.GetChannel(targetId) as IMessageChannel;
                if (channel == null)
                {
                    channel = await _discordClient.Rest.GetChannelAsync(targetId) as IMessageChannel;
                }

                if (channel == null)
                {
                    return (false, $"Salon Discord `{targetId}` introuvable.", null);
                }

                await channel.SendMessageAsync(embed: embed);
            }

            _logger.LogInformation("Morning Briefing envoyé avec succès sur Discord !");
            return (true, "Briefing envoyé avec succès sur Discord !", embed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'envoi du Morning Briefing sur Discord");
            return (false, $"Erreur Discord : {ex.Message}", null);
        }
    }

    private async Task<MorningBriefingResult> AnalyzeWithGeminiAsync(CommuteSlotSummary aller, CommuteSlotSummary retour, MorningBriefingSettings settings)
    {
        var apiKey = _techWatchConfigService.GetSettings().GeminiApiKey;
        var model = _techWatchConfigService.GetSettings().GeminiModel;
        if (string.IsNullOrWhiteSpace(model) || model.StartsWith("gemini-1.") || model == "gemini-2.0-flash")
        {
            model = "gemini-flash-latest";
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var prompt = $$"""
Tu es un assistant météo et mobilité vélo pour l'agglomération de Lyon.
Analyse les prévisions météo pour aujourd'hui pour les trajets vélo de Corentin :
- Trajet Aller ({{aller.TimeRange}}) : {{aller.MainEmoji}} {{aller.MainCondition}}, Température {{aller.MinTemp:F1}}°C à {{aller.MaxTemp:F1}}°C, Risque pluie {{aller.MaxRainProbability}}%, Cumul pluie {{aller.TotalPrecipitation:F1}}mm, Vent {{aller.MaxWindSpeed:F0}} km/h, Rafales max {{aller.MaxWindGusts:F0}} km/h.
- Trajet Retour ({{retour.TimeRange}}) : {{retour.MainEmoji}} {{retour.MainCondition}}, Température {{retour.MinTemp:F1}}°C à {{retour.MaxTemp:F1}}°C, Risque pluie {{retour.MaxRainProbability}}%, Cumul pluie {{retour.TotalPrecipitation:F1}}mm, Vent {{retour.MaxWindSpeed:F0}} km/h, Rafales max {{retour.MaxWindGusts:F0}} km/h.

Consignes strictes :
1. Évalue si c'est ok de prendre le vélo (Verdict : OK si sec et rafales < 40 km/h, PRUDENCE si pluie fine ou rafales 40-50 km/h, DECONSEILLE si pluie forte > 1mm/h, orage ou rafales > 50 km/h).
2. Fournis un résumé très direct et concis pour l'aller et pour le retour.
3. Donne un conseil pratique (équipement, prudence, veste, etc.).
4. Réponds UNIQUEMENT en format JSON valide avec ces 4 clés sans aucun bloc markdown additionnel :
{
  "verdict": "OK" ou "PRUDENCE" ou "DECONSEILLE",
  "verdictBanner": "Phrase d'accroche courte (ex: Conditions parfaites pour pédaler !)",
  "allerSummary": "Court résumé en 1-2 phrases (température, pluie, vent en km/h)",
  "retourSummary": "Court résumé en 1-2 phrases (température, pluie, vent en km/h)",
  "advice": "Conseil pratique court (< 100 caractères)"
}
""";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    }
                };

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    var text = doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var cleanedJson = text.Trim();
                        if (cleanedJson.StartsWith("```json"))
                        {
                            cleanedJson = cleanedJson.Substring(7);
                        }
                        if (cleanedJson.StartsWith("```"))
                        {
                            cleanedJson = cleanedJson.Substring(3);
                        }
                        if (cleanedJson.EndsWith("```"))
                        {
                            cleanedJson = cleanedJson.Substring(0, cleanedJson.Length - 3);
                        }
                        cleanedJson = cleanedJson.Trim();

                        using var parsedJson = JsonDocument.Parse(cleanedJson);
                        var root = parsedJson.RootElement;

                        var verdictStr = root.TryGetProperty("verdict", out var v) ? v.GetString() : "OK";
                        var banner = root.TryGetProperty("verdictBanner", out var b) ? b.GetString() : "Météo du jour";
                        var aSummary = root.TryGetProperty("allerSummary", out var a) ? a.GetString() : "";
                        var rSummary = root.TryGetProperty("retourSummary", out var r) ? r.GetString() : "";
                        var adv = root.TryGetProperty("advice", out var ad) ? ad.GetString() : "";

                        var verdict = verdictStr?.ToUpperInvariant() switch
                        {
                            "DECONSEILLE" => BikeVerdict.Deconseille,
                            "PRUDENCE" => BikeVerdict.Prudence,
                            _ => BikeVerdict.Ok
                        };

                        return new MorningBriefingResult
                        {
                            Verdict = verdict,
                            VerdictBanner = banner ?? "Météo du jour",
                            AllerSummary = aSummary ?? string.Empty,
                            RetourSummary = rSummary ?? string.Empty,
                            Advice = adv ?? string.Empty
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini a échoué pour le briefing. Utilisation de l'analyse locale.");
            }
        }

        // Repli logique sans IA
        return BuildLocalAnalysis(aller, retour);
    }

    private MorningBriefingResult BuildLocalAnalysis(CommuteSlotSummary aller, CommuteSlotSummary retour)
    {
        var maxPrecip = Math.Max(aller.TotalPrecipitation, retour.TotalPrecipitation);
        var maxGusts = Math.Max(aller.MaxWindGusts, retour.MaxWindGusts);
        var maxRainProb = Math.Max(aller.MaxRainProbability, retour.MaxRainProbability);

        BikeVerdict verdict;
        string banner;
        string advice;

        if (maxPrecip > 1.5 || maxGusts > 50)
        {
            verdict = BikeVerdict.Deconseille;
            banner = "🛑 Vélo déconseillé aujourd'hui : conditions météo difficiles";
            advice = maxGusts > 50 ? "Fortes rafales de vent dangereuses pour le vélo." : "Pluie soutenue prévue, privilégie les transports.";
        }
        else if (maxPrecip > 0.2 || maxRainProb > 40 || maxGusts > 35)
        {
            verdict = BikeVerdict.Prudence;
            banner = "⚠️ Vélo possible avec prudence : risque d'humidité ou vent";
            advice = "Prévois un imperméable et méfie-toi des chaussées glissantes.";
        }
        else
        {
            verdict = BikeVerdict.Ok;
            banner = "✅ Conditions idéales pour aller au travail à vélo !";
            advice = aller.MinTemp < 8 ? "Matinée fraîche, pense aux gants légers !" : "Bonne route et belle journée !";
        }

        return new MorningBriefingResult
        {
            Verdict = verdict,
            VerdictBanner = banner,
            AllerSummary = $"{aller.MainEmoji} **{aller.MainCondition}** ({aller.MinTemp:F0}°C - {aller.MaxTemp:F0}°C), pluie {aller.MaxRainProbability}%, rafales {aller.MaxWindGusts:F0} km/h.",
            RetourSummary = $"{retour.MainEmoji} **{retour.MainCondition}** ({retour.MinTemp:F0}°C - {retour.MaxTemp:F0}°C), pluie {retour.MaxRainProbability}%, rafales {retour.MaxWindGusts:F0} km/h.",
            Advice = advice
        };
    }

    private Embed BuildDiscordEmbed(MorningBriefingResult result, CommuteSlotSummary aller, CommuteSlotSummary retour, MorningBriefingSettings settings, bool isTest)
    {
        var (color, icon) = result.Verdict switch
        {
            BikeVerdict.Ok => (new Color(0x2e, 0xcc, 0x71), "🟢"),
            BikeVerdict.Prudence => (new Color(0xe6, 0x7e, 0x22), "🟠"),
            BikeVerdict.Deconseille => (new Color(0xe7, 0x4c, 0x3c), "🔴"),
            _ => (new Color(0x34, 0x98, 0xdb), "🔵")
        };

        var title = isTest
            ? $"🚴‍♂️ [Test] Trajet Vélo & Météo • Décines-Charpieu"
            : $"🚴‍♂️ Trajet Vélo & Météo • Décines-Charpieu";

        var builder = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription($"### {icon} {result.VerdictBanner}\n")
            .WithColor(color)
            .AddField($"🌅 Trajet Aller ({aller.TimeRange})", string.IsNullOrWhiteSpace(result.AllerSummary) ? $"{aller.MainEmoji} {aller.MainCondition}" : result.AllerSummary, inline: false)
            .AddField($"🌇 Trajet Retour ({retour.TimeRange})", string.IsNullOrWhiteSpace(result.RetourSummary) ? $"{retour.MainEmoji} {retour.MainCondition}" : result.RetourSummary, inline: false)
            .AddField("💡 Conseil de route", result.Advice, inline: false)
            .WithCurrentTimestamp()
            .WithFooter(footer => footer.Text = "Open-Meteo • Analysé par Gemini");

        return builder.Build();
    }
}
