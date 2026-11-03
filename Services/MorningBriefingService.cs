using System.Text.Json;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class MorningBriefingService(
    DiscordSocketClient discordClient,
    MorningBriefingConfigService configService,
    TechWatchConfigService techWatchConfigService,
    OpenMeteoService weatherService,
    ILogger<MorningBriefingService> logger)
{
    private record VerdictVisual(Color Color, string Icon);

    public async Task<BriefingExecutionResult> GenerateBriefingEmbedAsync(bool isManualTest = false, CancellationToken cancellationToken = default)
    {
        var settings = configService.GetSettings();
        var commute = await weatherService.GetCommuteWeatherAsync(settings, cancellationToken);
        if (commute == null)
        {
            return new BriefingExecutionResult(false, "Impossible de récupérer les prévisions météo Open-Meteo.");
        }

        var briefingResult = await AnalyzeWithGeminiAsync(commute.Aller, commute.Retour, settings);
        var embed = BuildDiscordEmbed(briefingResult, commute.Aller, commute.Retour, settings, isManualTest);
        return new BriefingExecutionResult(true, "Briefing généré avec succès.", embed);
    }

    public async Task<BriefingExecutionResult> ExecuteBriefingAsync(bool isManualTest = false, CancellationToken cancellationToken = default)
    {
        var settings = configService.GetSettings();
        if (!settings.IsEnabled && !isManualTest)
        {
            return new BriefingExecutionResult(false, "Le Morning Briefing est désactivé.");
        }

        if (discordClient.ConnectionState != ConnectionState.Connected)
        {
            return new BriefingExecutionResult(false, "Le bot Discord n'est pas connecté.");
        }

        if (!ulong.TryParse(settings.TargetId, out var targetId) || targetId == 0)
        {
            return new BriefingExecutionResult(false, "L'identifiant Discord cible est invalide.");
        }

        logger.LogInformation("Génération du Morning Briefing Vélo (Test: {IsTest})...", isManualTest);

        var genResult = await GenerateBriefingEmbedAsync(isManualTest, cancellationToken);
        if (!genResult.Success || genResult.Embed == null)
        {
            return genResult;
        }

        try
        {
            if (settings.DestinationType == DestinationType.UserDm)
            {
                IUser? user = discordClient.GetUser(targetId);
                user ??= await discordClient.Rest.GetUserAsync(targetId);

                if (user == null)
                {
                    return new BriefingExecutionResult(false, $"Utilisateur Discord `{targetId}` introuvable.");
                }

                var dm = await user.CreateDMChannelAsync();
                await dm.SendMessageAsync(embed: genResult.Embed);
            }
            else
            {
                var channel = discordClient.GetChannel(targetId) as IMessageChannel;
                channel ??= await discordClient.Rest.GetChannelAsync(targetId) as IMessageChannel;

                if (channel == null)
                {
                    return new BriefingExecutionResult(false, $"Salon Discord `{targetId}` introuvable.");
                }

                await channel.SendMessageAsync(embed: genResult.Embed);
            }

            logger.LogInformation("Morning Briefing envoyé avec succès sur Discord !");
            return new BriefingExecutionResult(true, "Briefing envoyé avec succès sur Discord !", genResult.Embed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'envoi du Morning Briefing sur Discord");
            return new BriefingExecutionResult(false, $"Erreur Discord : {ex.Message}");
        }
    }

    private async Task<MorningBriefingResult> AnalyzeWithGeminiAsync(CommuteSlotSummary aller, CommuteSlotSummary retour, MorningBriefingSettings settings)
    {
        var apiKey = techWatchConfigService.GetSettings().GeminiApiKey;
        var model = techWatchConfigService.GetSettings().GeminiModel;
        if (string.IsNullOrWhiteSpace(model) || model.StartsWith("gemini-1.") || model == "gemini-2.0-flash")
        {
            model = "gemini-flash-latest";
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BuildLocalAnalysis(aller, retour);
        }

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

            var client = new Client(apiKey: apiKey);
            var config = new GenerateContentConfig
            {
                ResponseMimeType = "application/json"
            };

            GenerateContentResponse? response = null;
            try
            {
                response = await client.Models.GenerateContentAsync(
                    model: model,
                    contents: prompt,
                    config: config
                );
            }
            catch (Exception ex) when (model != "gemini-flash-latest")
            {
                logger.LogWarning(ex, "Le modèle {Model} a échoué. Repli avec le SDK Google Gen AI sur gemini-flash-latest...", model);
                response = await client.Models.GenerateContentAsync(
                    model: "gemini-flash-latest",
                    contents: prompt,
                    config: config
                );
            }

            var text = response?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return BuildLocalAnalysis(aller, retour);
            }

            using var parsedJson = JsonDocument.Parse(text);
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Le SDK Google Gen AI a échoué pour le briefing. Utilisation de l'analyse locale.");
            return BuildLocalAnalysis(aller, retour);
        }
    }

    public static MorningBriefingResult BuildLocalAnalysis(CommuteSlotSummary aller, CommuteSlotSummary retour)
    {
        var maxPrecip = Math.Max(aller.TotalPrecipitation, retour.TotalPrecipitation);
        var maxGusts = Math.Max(aller.MaxWindGusts, retour.MaxWindGusts);
        var maxRainProb = Math.Max(aller.MaxRainProbability, retour.MaxRainProbability);

        if (maxPrecip > 1.5 || maxGusts > 50)
        {
            return new MorningBriefingResult
            {
                Verdict = BikeVerdict.Deconseille,
                VerdictBanner = "🛑 Vélo déconseillé aujourd'hui : conditions météo difficiles",
                AllerSummary = FormatSlotLine(aller),
                RetourSummary = FormatSlotLine(retour),
                Advice = maxGusts > 50 ? "Fortes rafales de vent dangereuses pour le vélo." : "Pluie soutenue prévue, privilégie les transports."
            };
        }

        if (maxPrecip > 0.2 || maxRainProb > 40 || maxGusts > 35)
        {
            return new MorningBriefingResult
            {
                Verdict = BikeVerdict.Prudence,
                VerdictBanner = "⚠️ Vélo possible avec prudence : risque d'humidité ou vent",
                AllerSummary = FormatSlotLine(aller),
                RetourSummary = FormatSlotLine(retour),
                Advice = "Prévois un imperméable et méfie-toi des chaussées glissantes."
            };
        }

        return new MorningBriefingResult
        {
            Verdict = BikeVerdict.Ok,
            VerdictBanner = "✅ Conditions idéales pour aller au travail à vélo !",
            AllerSummary = FormatSlotLine(aller),
            RetourSummary = FormatSlotLine(retour),
            Advice = aller.MinTemp < 8 ? "Matinée fraîche, pense aux gants légers !" : "Bonne route et belle journée !"
        };
    }

    private static string FormatSlotLine(CommuteSlotSummary slot) =>
        $"{slot.MainEmoji} **{slot.MainCondition}** ({slot.MinTemp:F0}°C - {slot.MaxTemp:F0}°C), pluie {slot.MaxRainProbability}%, rafales {slot.MaxWindGusts:F0} km/h.";

    private static Embed BuildDiscordEmbed(MorningBriefingResult result, CommuteSlotSummary aller, CommuteSlotSummary retour, MorningBriefingSettings settings, bool isTest)
    {
        var visual = result.Verdict switch
        {
            BikeVerdict.Ok => new VerdictVisual(new Color(0x2e, 0xcc, 0x71), "🟢"),
            BikeVerdict.Prudence => new VerdictVisual(new Color(0xe6, 0x7e, 0x22), "🟠"),
            BikeVerdict.Deconseille => new VerdictVisual(new Color(0xe7, 0x4c, 0x3c), "🔴"),
            _ => new VerdictVisual(new Color(0x34, 0x98, 0xdb), "🔵")
        };

        var title = isTest
            ? "🚴‍♂️ [Test] Trajet Vélo & Météo • Décines-Charpieu"
            : "🚴‍♂️ Trajet Vélo & Météo • Décines-Charpieu";

        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription($"### {visual.Icon} {result.VerdictBanner}\n")
            .WithColor(visual.Color)
            .AddField($"🌅 Trajet Aller ({aller.TimeRange})", string.IsNullOrWhiteSpace(result.AllerSummary) ? $"{aller.MainEmoji} {aller.MainCondition}" : result.AllerSummary, inline: false)
            .AddField($"🌇 Trajet Retour ({retour.TimeRange})", string.IsNullOrWhiteSpace(result.RetourSummary) ? $"{retour.MainEmoji} {retour.MainCondition}" : result.RetourSummary, inline: false)
            .AddField("💡 Conseil de route", result.Advice, inline: false)
            .WithCurrentTimestamp()
            .WithFooter(footer => footer.Text = "Open-Meteo • Analysé par Gemini")
            .Build();
    }
}
