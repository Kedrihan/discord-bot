using DiscordBot.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class MorningBriefingScheduler : BackgroundService
{
    private readonly MorningBriefingConfigService _configService;
    private readonly MorningBriefingService _briefingService;
    private readonly ILogger<MorningBriefingScheduler> _logger;
    private string? _lastExecutionKey;

    public MorningBriefingScheduler(
        MorningBriefingConfigService configService,
        MorningBriefingService briefingService,
        ILogger<MorningBriefingScheduler> logger)
    {
        _configService = configService;
        _briefingService = briefingService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Planificateur du Morning Briefing Vélo démarré.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                CheckAndTrigger();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur dans la boucle du planificateur de Morning Briefing");
            }
        }
    }

    private void CheckAndTrigger()
    {
        var settings = _configService.GetSettings();
        if (!settings.IsEnabled)
        {
            return;
        }

        var now = DateTime.Now;
        var currentDay = now.DayOfWeek;

        if (settings.ScheduleDays == null || !settings.ScheduleDays.Contains(currentDay))
        {
            return;
        }

        if (now.Hour == settings.ScheduleTime.Hour && now.Minute == settings.ScheduleTime.Minute)
        {
            var executionKey = $"{now:yyyy-MM-dd-HH-mm}";
            if (_lastExecutionKey == executionKey)
            {
                return;
            }

            _lastExecutionKey = executionKey;
            _logger.LogInformation("Déclenchement planifié du Morning Briefing Vélo ({Day} {Time})", currentDay, settings.ScheduleTime.ToString("HH:mm"));

            _ = Task.Run(async () =>
            {
                try
                {
                    var (success, msg, _) = await _briefingService.ExecuteBriefingAsync(isManualTest: false);
                    if (!success)
                    {
                        _logger.LogWarning("Échec de l'envoi du Morning Briefing planifié : {Reason}", msg);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'exécution automatique du Morning Briefing");
                }
            });
        }
    }
}
