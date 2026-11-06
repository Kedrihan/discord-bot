using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class UptimeMonitorScheduler(
    UptimeMonitorService uptimeMonitorService,
    UptimeConfigService configService,
    TimeProvider timeProvider,
    ILogger<UptimeMonitorScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Démarrage du planificateur de surveillance d'uptime.");

        try
        {
            // Délai initial de stabilisation au démarrage du bot
            await Task.Delay(TimeSpan.FromSeconds(10), timeProvider, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = configService.GetSettings();
                if (settings.IsEnabled)
                {
                    try
                    {
                        await uptimeMonitorService.CheckAllTargetsAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Erreur inattendue lors de l'exécution de la surveillance d'uptime.");
                    }
                }

                var intervalSeconds = Math.Clamp(settings.CheckIntervalSeconds, 10, 86400);
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt normal
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Arrêt critique du planificateur d'uptime.");
        }

        logger.LogInformation("Planificateur de surveillance d'uptime arrêté.");
    }
}
