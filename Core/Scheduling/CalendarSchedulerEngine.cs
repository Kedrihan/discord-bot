using System.Collections.Concurrent;

namespace DiscordBot.Services;

public class CalendarSchedulerEngine(
    IEnumerable<ICalendarJob> jobs,
    TimeProvider timeProvider,
    ILogger<CalendarSchedulerEngine> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, string> _lastExecutionKeyByJob = new();
    private readonly ConcurrentDictionary<string, object> _jobGates = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _jobSemaphores = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Moteur de planification calendaire démarré.");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Exécution asynchrone découplée pour ne pas bloquer la pulsation du PeriodicTimer
                // en cas de job à exécution longue.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await CheckAndTriggerJobsAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // Arrêt normal
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Erreur inattendue lors de l'exécution asynchrone des jobs calendaires");
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur inattendue dans la boucle du planificateur calendaire");
            }
        }
    }

    private static readonly TimeZoneInfo ParisTimeZone = ResolveParisTimeZone();

    private static TimeZoneInfo ResolveParisTimeZone()
    {
        try
        {
            // Standard IANA (Linux / Docker)
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback Windows
            return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        }
    }

    internal static DateTimeOffset GetParisTime(TimeProvider provider)
    {
        var utcNow = provider.GetUtcNow();
        return TimeZoneInfo.ConvertTime(utcNow, ParisTimeZone);
    }

    internal async Task CheckAndTriggerJobsAsync(CancellationToken cancellationToken = default)
    {
        var now = GetParisTime(timeProvider);
        var currentDay = now.DayOfWeek;
        var executionKey = $"{now:yyyy-MM-dd-HH-mm}";

        var runningTasks = new List<Task>();

        foreach (var job in jobs)
        {
            try
            {
                var schedule = job.GetSchedule();
                if (!schedule.IsEnabled)
                {
                    continue;
                }

                if (schedule.ScheduleDays == null || schedule.ScheduleDays.Count == 0 || !schedule.ScheduleDays.Contains(currentDay))
                {
                    continue;
                }

                if (now.Hour != schedule.ScheduleTime.Hour || now.Minute != schedule.ScheduleTime.Minute)
                {
                    continue;
                }

                var gate = _jobGates.GetOrAdd(job.JobName, _ => new object());
                lock (gate)
                {
                    if (_lastExecutionKeyByJob.TryGetValue(job.JobName, out var lastKey) && lastKey == executionKey)
                    {
                        continue;
                    }

                    _lastExecutionKeyByJob[job.JobName] = executionKey;
                }

                logger.LogInformation("Déclenchement planifié du job '{JobName}' ({Day} {Time})", job.JobName, currentDay, schedule.ScheduleTime.ToString("HH:mm"));

                runningTasks.Add(ExecuteJobSafelyAsync(job, cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur lors de l'évaluation du planning pour le job '{JobName}'", job.JobName);
            }
        }

        if (runningTasks.Count > 0)
        {
            await Task.WhenAll(runningTasks);
        }
    }

    private async Task ExecuteJobSafelyAsync(ICalendarJob job, CancellationToken cancellationToken)
    {
        var semaphore = _jobSemaphores.GetOrAdd(job.JobName, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(0, cancellationToken))
        {
            logger.LogWarning("Le job '{JobName}' est déjà en cours d'exécution. Déclenchement ignoré pour éviter toute exécution concurrente.", job.JobName);
            return;
        }

        try
        {
            // Protection anti-DoS : limite d'exécution maximale de 10 minutes par job
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10), timeProvider);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var result = await job.ExecuteAsync(linkedCts.Token);
            if (!result.Success)
            {
                logger.LogWarning("Échec de l'envoi pour le job '{JobName}' : {Reason}", job.JobName, result.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Arrêt normal lors de l'annulation du service
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Le job '{JobName}' a dépassé le délai d'exécution maximal autorisé (10 minutes) et a été interrompu.", job.JobName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de l'exécution automatique du job '{JobName}'", job.JobName);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public override void Dispose()
    {
        foreach (var semaphore in _jobSemaphores.Values)
        {
            semaphore.Dispose();
        }
        _jobSemaphores.Clear();
        base.Dispose();
    }
}
