namespace DiscordBot.DTO;

public record CalendarScheduleDto(
    bool IsEnabled,
    IReadOnlyList<DayOfWeek>? ScheduleDays,
    TimeOnly ScheduleTime);
