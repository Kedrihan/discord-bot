using DiscordBot.DTO;

namespace DiscordBot.Services;

public class TechWatchJob(
    TechWatchConfigService configService,
    TechWatchService techWatchService) : ICalendarJob
{
    public string JobName => "Veille Technologique";

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
        var result = await techWatchService.ExecuteWatchAsync(isManualTest: false, cancellationToken);
        return result.Success
            ? CalendarJobExecutionResultDto.Succeeded(result.Message)
            : CalendarJobExecutionResultDto.Failed(result.Message);
    }
}
