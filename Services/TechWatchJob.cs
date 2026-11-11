using DiscordBot.Models;

namespace DiscordBot.Services;

public class TechWatchJob(
    TechWatchConfigService configService,
    TechWatchService techWatchService) : ICalendarJob
{
    public string JobName => "Veille Technologique";

    public CalendarSchedule GetSchedule()
    {
        var settings = configService.GetSettings();
        return new CalendarSchedule(
            settings.IsEnabled,
            settings.ScheduleDays,
            settings.ScheduleTime);
    }

    public async Task<CalendarJobExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = await techWatchService.ExecuteWatchAsync(isManualTest: false, cancellationToken);
        return result.Success
            ? CalendarJobExecutionResult.Succeeded(result.Message)
            : CalendarJobExecutionResult.Failed(result.Message);
    }
}
