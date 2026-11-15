namespace DiscordBot.DTO.Common;

public record CalendarScheduleDto(
    bool IsEnabled,
    IReadOnlyList<DayOfWeek>? ScheduleDays,
    TimeOnly ScheduleTime);
