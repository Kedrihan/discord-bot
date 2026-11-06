using System.Diagnostics;
using System.Runtime.InteropServices;
using Discord;
using Discord.WebSocket;
using DiscordBot.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class SystemHealthService(
    DiscordSocketClient discordClient,
    IServiceProvider serviceProvider,
    BotSettingsService botSettingsService,
    TimeProvider timeProvider,
    ILogger<SystemHealthService> logger)
{
    public SystemMetrics GetSystemMetrics()
    {
        logger.LogDebug("Collecte des métriques système du conteneur.");

        using var process = Process.GetCurrentProcess();
        var workingSet = process.WorkingSet64;
        var threadCount = process.Threads.Count;

        var gcTotal = GC.GetTotalMemory(forceFullCollection: false);
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);

        var startTime = botSettingsService.StartTime;
        var uptime = timeProvider.GetUtcNow() - startTime;

        var dotNetVersion = RuntimeInformation.FrameworkDescription;
        var osDescription = RuntimeInformation.OSDescription;
        var architecture = RuntimeInformation.ProcessArchitecture.ToString();

        return new SystemMetrics(
            Uptime: uptime,
            StartTime: startTime,
            WorkingSetBytes: workingSet,
            GcTotalBytes: gcTotal,
            Gen0Collections: gen0,
            Gen1Collections: gen1,
            Gen2Collections: gen2,
            ThreadCount: threadCount,
            DotNetVersion: dotNetVersion,
            OsDescription: osDescription,
            Architecture: architecture
        );
    }

    public IReadOnlyList<ServiceHealthInfo> GetServicesHealth()
    {
        var services = new List<ServiceHealthInfo>();

        // 1. Passerelle Discord
        var isDiscordConnected = discordClient.ConnectionState == ConnectionState.Connected;
        services.Add(new ServiceHealthInfo(
            Name: "Passerelle Discord",
            IsHealthy: isDiscordConnected,
            StatusText: isDiscordConnected ? $"Connecté ({discordClient.Latency} ms)" : $"Déconnecté ({discordClient.ConnectionState})",
            Details: $"Serveurs: {discordClient.Guilds.Count} | Utilisateur: {discordClient.CurrentUser?.Username ?? "N/A"}"
        ));

        // 2. Twitch EventSub WebSocket
        var twitchEventSub = serviceProvider.GetService<TwitchEventSubService>();
        if (twitchEventSub != null)
        {
            var isTwitchHealthy = twitchEventSub.Status == TwitchEventSubStatus.Connected;
            services.Add(new ServiceHealthInfo(
                Name: "Twitch EventSub",
                IsHealthy: isTwitchHealthy,
                StatusText: isTwitchHealthy ? "Connecté (WebSocket)" : twitchEventSub.Status.ToString(),
                Details: twitchEventSub.SessionId != null ? $"Session: {twitchEventSub.SessionId}" : "Aucune session active"
            ));
        }

        // 3. Planificateur Veille Technologique
        var techWatchConfig = serviceProvider.GetService<TechWatchConfigService>();
        if (techWatchConfig != null)
        {
            var settings = techWatchConfig.GetSettings();
            services.Add(new ServiceHealthInfo(
                Name: "Veille Technologique",
                IsHealthy: settings.IsEnabled,
                StatusText: settings.IsEnabled ? $"Actif ({settings.ScheduleTime:HH\\:mm})" : "Désactivé",
                Details: $"Thématiques: {settings.Topics.Count} | Jours: {settings.ScheduleDays.Count}"
            ));
        }

        // 4. Planificateur Morning Briefing
        var briefingConfig = serviceProvider.GetService<MorningBriefingConfigService>();
        if (briefingConfig != null)
        {
            var settings = briefingConfig.GetSettings();
            services.Add(new ServiceHealthInfo(
                Name: "Briefing Matin",
                IsHealthy: settings.IsEnabled,
                StatusText: settings.IsEnabled ? $"Actif ({settings.ScheduleTime:HH\\:mm})" : "Désactivé",
                Details: settings.Address
            ));
        }

        // 5. Planificateur Uptime Monitor
        var uptimeConfig = serviceProvider.GetService<UptimeConfigService>();
        if (uptimeConfig != null)
        {
            var settings = uptimeConfig.GetSettings();
            services.Add(new ServiceHealthInfo(
                Name: "Uptime Monitor",
                IsHealthy: settings.IsEnabled,
                StatusText: settings.IsEnabled ? $"Actif ({settings.CheckIntervalSeconds}s)" : "Désactivé",
                Details: $"Cibles: {settings.Targets.Count(t => t.IsEnabled)}/{settings.Targets.Count} actives"
            ));
        }

        return services;
    }
}
