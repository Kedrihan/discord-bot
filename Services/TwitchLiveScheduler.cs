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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Service Twitch Live démarré (Mode EventSub WebSocket).");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var settings = configService.GetSettings();
        if (settings.IsEnabled)
        {
            try
            {
                logger.LogInformation("Démarrage du client EventSub WebSocket Twitch...");
                await eventSubService.StartAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors du démarrage d'EventSub WebSocket.");
            }

            try
            {
                logger.LogInformation("Exécution de la synchronisation initiale unique des streams Twitch...");
                await liveService.CheckStreamsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de la synchronisation initiale des streams Twitch.");
            }
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Arrêt du service Twitch Live EventSub.");
        }
        finally
        {
            await eventSubService.StopAsync(CancellationToken.None);
        }
    }
}
