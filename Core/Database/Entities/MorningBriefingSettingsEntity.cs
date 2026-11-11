using DiscordBot.DTO;

namespace DiscordBot.Core.Database.Entities;

public class MorningBriefingSettingsEntity
{
    public int Id { get; set; }
    public bool IsEnabled { get; set; }
    public List<DayOfWeek> ScheduleDays { get; set; } = [];
    public TimeOnly ScheduleTime { get; set; }
    public DestinationTypeDto DestinationType { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int MorningStartHour { get; set; }
    public int MorningEndHour { get; set; }
    public int EveningStartHour { get; set; }
    public int EveningEndHour { get; set; }
}
