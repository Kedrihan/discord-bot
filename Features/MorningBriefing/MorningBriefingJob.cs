using DiscordBot.DTO;

namespace DiscordBot.Services;

public class MorningBriefingJob(
    MorningBriefingConfigService configService,
    MorningBriefingService briefingService) : ICalendarJob
{
    public string JobName => "Morning Briefing Vélo";

    public CalendarScheduleDto GetSchedule()
    {
        var settings = configService.GetSettings();
        return new CalendarScheduleDto(
            settings.IsEnabled,
            settings.ScheduleDays,
            settings.ScheduleTime);
    }

    public async Task<CalendarJobExecutionResultDto> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var result = await briefingService.ExecuteBriefingAsync(isManualTest: false, cancellationToken);
        return result.Success
            ? CalendarJobExecutionResultDto.Succeeded(result.Message)
            : CalendarJobExecutionResultDto.Failed(result.Message);
    }
}
