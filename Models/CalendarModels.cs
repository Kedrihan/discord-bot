namespace DiscordBot.Models;

public record CalendarSchedule(
    bool IsEnabled,
    IReadOnlyList<DayOfWeek>? ScheduleDays,
    TimeOnly ScheduleTime);

public record CalendarJobExecutionResult(bool Success, string Message)
{
    public static CalendarJobExecutionResult Succeeded(string message = "Exécution réussie") => new(true, message);
    public static CalendarJobExecutionResult Failed(string message) => new(false, message);
}
