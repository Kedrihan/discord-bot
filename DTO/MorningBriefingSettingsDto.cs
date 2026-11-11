namespace DiscordBot.DTO;

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
    public string TargetId { get; set; } = "116893981526196227";

    // Localisation
    public string Address { get; set; } = "19 Impasse de Vérone, 69150 Décines-Charpieu";
    public double Latitude { get; set; } = 45.7678;
    public double Longitude { get; set; } = 4.9619;

    // Créneaux horaires de trajet
    public int MorningStartHour { get; set; } = 7;
    public int MorningEndHour { get; set; } = 8;
    public int EveningStartHour { get; set; } = 17;
    public int EveningEndHour { get; set; } = 18;
}
