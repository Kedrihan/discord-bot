namespace DiscordBot.Services;

public class TwitchClipScheduler(
    TwitchConfigService configService,
    TwitchClipService clipService,
    TimeProvider timeProvider,
    ILogger<TwitchClipScheduler> logger) : BackgroundService
{
    public bool IsWaitingForInitialDelay { get; internal set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Service Twitch Clip Scheduler démarré.");

        try
        {
            var initialDelay = Task.Delay(TimeSpan.FromSeconds(10), timeProvider, stoppingToken);
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
        if (initialSettings.IsEnabled && initialSettings.IsClipsEnabled)
        {
            try
            {
                logger.LogInformation("Vérification initiale des clips Twitch au démarrage...");
                await clipService.CheckNewClipsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la vérification initiale des clips Twitch.");
            }
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = configService.GetSettings();
                var intervalSeconds = Math.Clamp(settings.ClipCheckIntervalSeconds, 15, 3600);

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds), timeProvider);
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
                if (!currentSettings.IsEnabled || !currentSettings.IsClipsEnabled)
                {
                    continue;
                }

                try
                {
                    logger.LogDebug("Exécution de la vérification périodique des clips Twitch...");
                    await clipService.CheckNewClipsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erreur lors de la vérification périodique des clips Twitch.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt normal
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Erreur inattendue dans la boucle du scheduler de clips Twitch.");
        }

        logger.LogInformation("Service Twitch Clip Scheduler arrêté.");
    }
}
