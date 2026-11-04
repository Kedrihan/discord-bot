using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TwitchLiveScheduler(
    TwitchConfigService configService,
    TwitchLiveService liveService,
    TimeProvider timeProvider,
    ILogger<TwitchLiveScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Planificateur des notifications Twitch Live démarré.");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = configService.GetSettings();
            var interval = Math.Clamp(settings.CheckIntervalSeconds, 15, 3600);

            try
            {
                if (settings.IsEnabled)
                {
                    await liveService.CheckStreamsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors du cycle de vérification Twitch Live.");
            }

            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval), timeProvider);
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
