using DiscordBot.Models;

namespace DiscordBot.Services;

public interface ICalendarJob
{
    string JobName { get; }
    CalendarSchedule GetSchedule();
    Task<CalendarJobExecutionResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
