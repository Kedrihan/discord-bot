using DiscordBot.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class MorningBriefingScheduler(
    MorningBriefingConfigService configService,
    MorningBriefingService briefingService,
    TimeProvider timeProvider,
    ILogger<MorningBriefingScheduler> logger) : BackgroundService
{
    private string? _lastExecutionKey;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Planificateur du Morning Briefing Vélo démarré.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                CheckAndTrigger();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur dans la boucle du planificateur de Morning Briefing");
            }
        }
    }

    private void CheckAndTrigger()
    {
        var settings = configService.GetSettings();
        if (!settings.IsEnabled)
        {
            return;
        }

        var now = timeProvider.GetLocalNow();
        var currentDay = now.DayOfWeek;

        if (settings.ScheduleDays == null || !settings.ScheduleDays.Contains(currentDay))
        {
            return;
        }

        if (now.Hour != settings.ScheduleTime.Hour || now.Minute != settings.ScheduleTime.Minute)
        {
            return;
        }

        var executionKey = $"{now:yyyy-MM-dd-HH-mm}";
        if (_lastExecutionKey == executionKey)
        {
            return;
        }

        _lastExecutionKey = executionKey;
        logger.LogInformation("Déclenchement planifié du Morning Briefing Vélo ({Day} {Time})", currentDay, settings.ScheduleTime.ToString("HH:mm"));

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await briefingService.ExecuteBriefingAsync(isManualTest: false);
                if (!result.Success)
                {
                    logger.LogWarning("Échec de l'envoi du Morning Briefing planifié : {Reason}", result.Message);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de l'exécution automatique du Morning Briefing");
            }
        });
    }
}
