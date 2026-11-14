namespace DiscordBot.Features.YouTube;

public class YouTubeScheduler(
    YouTubeConfigService configService,
    YouTubeNotificationService notificationService,
    TimeProvider timeProvider,
    ILogger<YouTubeScheduler> logger) : BackgroundService
{
    public bool IsWaitingForInitialDelay { get; internal set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Service YouTube Scheduler démarré.");

        try
        {
            var initialDelay = Task.Delay(TimeSpan.FromSeconds(15), timeProvider, stoppingToken);
            IsWaitingForInitialDelay = true;
            await initialDelay;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            IsWaitingForInitialDelay = false;
        }

        var initialSettings = configService.GetSettings();
        if (initialSettings.IsEnabled)
        {
            try
            {
                logger.LogInformation("Vérification initiale des vidéos YouTube au démarrage...");
                await notificationService.CheckNewVideosAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la vérification initiale des vidéos YouTube.");
            }
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = configService.GetSettings();
                var intervalMinutes = Math.Clamp(settings.CheckIntervalMinutes, 1, 1440);

                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes), timeProvider);
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
                    logger.LogDebug("Exécution de la vérification périodique des vidéos YouTube...");
                    await notificationService.CheckNewVideosAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erreur lors de la vérification périodique des vidéos YouTube.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt normal
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Erreur inattendue dans la boucle du scheduler YouTube.");
        }

        logger.LogInformation("Service YouTube Scheduler arrêté.");
    }
}
