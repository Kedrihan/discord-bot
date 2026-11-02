using DiscordBot.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class TechWatchScheduler : BackgroundService
{
    private readonly TechWatchConfigService _configService;
    private readonly TechWatchService _techWatchService;
    private readonly ILogger<TechWatchScheduler> _logger;
    private string? _lastExecutionKey;

    public TechWatchScheduler(
        TechWatchConfigService configService,
        TechWatchService techWatchService,
        ILogger<TechWatchScheduler> logger)
    {
        _configService = configService;
        _techWatchService = techWatchService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Planificateur de la veille technologique démarré.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                CheckAndTrigger();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur dans la boucle du planificateur de veille techno");
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
            // Clé unique pour ne pas exécuter deux fois la même minute
            var executionKey = $"{now:yyyy-MM-dd-HH-mm}";
            if (_lastExecutionKey == executionKey)
            {
                return;
            }

            _lastExecutionKey = executionKey;
            _logger.LogInformation("Déclenchement planifié de la veille technologique ({Day} {Time})", currentDay, settings.ScheduleTime.ToString("HH:mm"));

            _ = Task.Run(async () =>
            {
                try
                {
                    var (success, msg) = await _techWatchService.ExecuteWatchAsync(isManualTest: false);
                    if (!success)
                    {
                        _logger.LogWarning("Échec de l'envoi de la veille techno planifiée : {Reason}", msg);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de l'exécution automatique de la veille techno");
                }
            });
        }
    }
}
