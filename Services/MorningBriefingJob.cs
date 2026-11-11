using DiscordBot.Models;

namespace DiscordBot.Services;

public class MorningBriefingJob(
    MorningBriefingConfigService configService,
    MorningBriefingService briefingService) : ICalendarJob
{
    public string JobName => "Morning Briefing Vélo";

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
        var result = await briefingService.ExecuteBriefingAsync(isManualTest: false, cancellationToken);
        return result.Success
            ? CalendarJobExecutionResult.Succeeded(result.Message)
            : CalendarJobExecutionResult.Failed(result.Message);
    }
}
