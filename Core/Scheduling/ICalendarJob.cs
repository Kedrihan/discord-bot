using DiscordBot.DTO.Common;

namespace DiscordBot.Core.Scheduling;

public interface ICalendarJob
{
    string JobName { get; }
    CalendarScheduleDto GetSchedule();
    Task<CalendarJobExecutionResultDto> ExecuteAsync(CancellationToken cancellationToken = default);
}
