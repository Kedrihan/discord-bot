using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TwitchLiveScheduler(
    TwitchConfigService configService,
    TwitchLiveService liveService,
    TwitchEventSubService eventSubService,
    TimeProvider timeProvider,
    ILogger<TwitchLiveScheduler> logger) : BackgroundService
{
    public bool IsWaitingForInitialDelay { get; internal set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Service Twitch Live démarré (Mode Hybride EventSub / Polling).");

        try
        {
            var initialDelay = Task.Delay(TimeSpan.FromSeconds(5), timeProvider, stoppingToken);
            IsWaitingForInitialDelay = true;
            await initialDelay;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var initialSettings = configService.GetSettings();
        if (initialSettings.IsEnabled)
        {
            try
            {
                logger.LogInformation("Démarrage initial du client EventSub WebSocket Twitch...");
                await eventSubService.StartAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors du démarrage d'EventSub WebSocket.");
            }

            try
            {
                logger.LogInformation("Exécution de la synchronisation initiale des streams Twitch...");
                await liveService.CheckStreamsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la synchronisation initiale des streams Twitch.");
            }
        }

        var safetyCheckInterval = TimeSpan.FromMinutes(15);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = configService.GetSettings();
                var pollInterval = TimeSpan.FromSeconds(Math.Clamp(settings.CheckIntervalSeconds, 15, 3600));
                var interval = eventSubService.IsConnected ? safetyCheckInterval : pollInterval;

                using var timer = new PeriodicTimer(interval, timeProvider);
                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var currentSettings = configService.GetSettings();
                if (!currentSettings.IsEnabled)
                {
                    continue;
                }

                try
                {
                    if (eventSubService.IsConnected)
                    {
                        logger.LogInformation("Exécution de la vérification de sécurité périodique des streams Twitch...");
                        await liveService.CheckStreamsAsync(stoppingToken);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(currentSettings.UserAccessToken))
                        {
                            logger.LogInformation("Tentative de reconnexion à EventSub WebSocket...");
                            await eventSubService.StartAsync(stoppingToken);
                        }

                        logger.LogInformation("Exécution du polling périodique des streams Twitch...");
                        await liveService.CheckStreamsAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erreur lors de la vérification des streams Twitch dans le scheduler.");
                }
            }
        }
        finally
        {
            logger.LogInformation("Arrêt du service Twitch Live.");
            await eventSubService.StopAsync(CancellationToken.None);
        }
    }
}
