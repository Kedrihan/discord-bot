using DiscordBot.DTO.Common;

namespace DiscordBot.DTO.MorningBriefing;

public class MorningBriefingSettingsDto
{
    public bool IsEnabled { get; set; } = true;

    // Planification
    public List<DayOfWeek> ScheduleDays { get; set; } =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday
    ];
    public TimeOnly ScheduleTime { get; set; } = new(7, 0);

    // Destinataire
    public DestinationTypeDto DestinationType { get; set; } = DestinationTypeDto.UserDm;
    public string TargetId { get; set; } = string.Empty;

    // Localisation
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // Créneaux horaires de trajet
    public int MorningStartHour { get; set; } = 7;
    public int MorningEndHour { get; set; } = 8;
    public int EveningStartHour { get; set; } = 17;
    public int EveningEndHour { get; set; } = 18;
}
