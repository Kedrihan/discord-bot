using Discord;
using Discord.Interactions;
using DiscordBot.Models;
using DiscordBot.Services;

namespace DiscordBot.Modules;

public class GeneralModule(
    MorningBriefingService morningBriefingService,
    SystemHealthService systemHealthService,
    UptimeConfigService uptimeConfigService,
    UptimeStateService uptimeStateService) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("ping", "Vérifie si le bot répond et affiche sa latence")]
    public async Task PingAsync()
    {
        var latency = Context.Client.Latency;

        var embed = new EmbedBuilder()
            .WithTitle("🏓 Pong !")
            .WithDescription($"Le bot en **.NET 10** fonctionne parfaitement sur Docker !\n\n⏱️ **Latence :** `{latency} ms`")
            .WithColor(Color.Green)
            .WithCurrentTimestamp()
            .WithFooter(footer => footer.Text = "DiscordBot .NET")
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("briefing", "Affiche le briefing météo et le verdict vélo pour aujourd'hui")]
    public async Task BriefingAsync()
    {
        await DeferAsync();

        var result = await morningBriefingService.GenerateBriefingEmbedAsync(isManualTest: false);
        if (result.Success && result.Embed != null)
        {
            await FollowupAsync(embed: result.Embed);
            return;
        }

        await FollowupAsync($"⚠️ Impossible de générer le briefing : {result.Message}");
    }

    [SlashCommand("health", "Affiche l'état de santé des services et le monitoring d'uptime")]
    public async Task HealthAsync()
    {
        var services = systemHealthService.GetServicesHealth();
        var uptimeSettings = uptimeConfigService.GetSettings();
        var uptimeState = uptimeStateService.GetState();

        var servicesSummary = string.Join("\n", services.Select(s =>
        {
            var icon = s.IsHealthy ? "🟢" : "🔴";
            return $"{icon} **{s.Name}** : {s.StatusText}";
        }));

        var enabledTargets = uptimeSettings.Targets.Where(t => t.IsEnabled).ToList();
        var targetStates = uptimeState.TargetStates.ToDictionary(s => s.TargetId);

        var downCount = 0;
        var targetsLines = new List<string>();

        foreach (var t in enabledTargets)
        {
            targetStates.TryGetValue(t.Id, out var state);
            var status = state?.Status ?? TargetStatus.Pending;
            if (status == TargetStatus.Down) downCount++;

            var icon = status switch
            {
                TargetStatus.Up => "🟢",
                TargetStatus.Down => "🔴",
                _ => "🟡"
            };

            var proto = t.Protocol == TargetProtocol.Http ? "HTTP" : $"TCP:{t.Port}";
            var latencyText = state?.LastResponseTimeMs > 0 ? $"{state.LastResponseTimeMs} ms" : "—";
            var errorSuffix = status == TargetStatus.Down && !string.IsNullOrWhiteSpace(state?.LastErrorMessage)
                ? $" ({UptimeMonitorService.SanitizeUntrustedText(state.LastErrorMessage)})"
                : "";

            var safeName = UptimeMonitorService.SanitizeUntrustedText(t.Name);
            targetsLines.Add($"{icon} **{safeName}** (`{proto}`) : `{latencyText}`{errorSuffix}");
        }

        var targetsSummary = targetsLines.Count > 0
            ? string.Join("\n", targetsLines)
            : "*(Aucune cible active configurée)*";

        var allServicesHealthy = services.All(s => s.IsHealthy);
        var embedColor = downCount > 0
            ? Color.Red
            : allServicesHealthy ? Color.Green : Color.Gold;

        var embed = new EmbedBuilder()
            .WithTitle("🩺 Santé des Services & Monitoring")
            .WithColor(embedColor)
            .AddField("⚙️ Services Internes", servicesSummary, inline: false)
            .AddField($"🌐 Cibles Réseau ({enabledTargets.Count - downCount}/{enabledTargets.Count} opérationnelles)", targetsSummary, inline: false)
            .WithFooter("DiscordBot Observability")
            .WithCurrentTimestamp()
            .Build();

        await RespondAsync(embed: embed, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("stats", "Affiche les statistiques système et métriques du conteneur")]
    public async Task StatsAsync()
    {
        var metrics = systemHealthService.GetSystemMetrics();

        var workingSetMb = metrics.WorkingSetBytes / (1024.0 * 1024.0);
        var gcMb = metrics.GcTotalBytes / (1024.0 * 1024.0);

        var uptimeFormatted = UptimeMonitorService.FormatDuration(metrics.Uptime);
        var startTimeUnix = metrics.StartTime.ToUnixTimeSeconds();

        var embed = new EmbedBuilder()
            .WithTitle("📊 Statistiques Conteneur & Bot")
            .WithColor(Color.Blue)
            .AddField("⏱️ Uptime & Démarrage", $"`{uptimeFormatted}` (démarré <t:{startTimeUnix}:R>)", inline: false)
            .AddField("🧠 Mémoire Vive", $"WorkingSet : `{workingSetMb:F1} Mo`\nTas GC : `{gcMb:F1} Mo`", inline: true)
            .AddField("🔄 Garbage Collector", $"Gen 0 : `{metrics.Gen0Collections}`\nGen 1 : `{metrics.Gen1Collections}`\nGen 2 : `{metrics.Gen2Collections}`", inline: true)
            .AddField("🧵 Threads Actifs", $"`{metrics.ThreadCount}` threads", inline: true)
            .AddField("⚙️ Plateforme & Runtime", $"**Runtime :** `{metrics.DotNetVersion}`\n**Système :** `{metrics.OsDescription}`\n**Architecture :** `{metrics.Architecture}`", inline: false)
            .WithFooter("DiscordBot .NET 10 Observability")
            .WithCurrentTimestamp()
            .Build();

        await RespondAsync(embed: embed);
    }
}
