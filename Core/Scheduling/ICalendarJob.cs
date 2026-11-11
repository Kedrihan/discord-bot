using DiscordBot.DTO;

namespace DiscordBot.Services;

public interface ICalendarJob
{
    string JobName { get; }
    CalendarScheduleDto GetSchedule();
    Task<CalendarJobExecutionResultDto> ExecuteAsync(CancellationToken cancellationToken = default);
}
