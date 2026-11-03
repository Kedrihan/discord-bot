using Discord;

namespace DiscordBot.Models;

public enum BikeVerdict
{
    Ok,
    Prudence,
    Deconseille
}

public class HourlyWeatherSlot
{
    public int Hour { get; set; }
    public double Temperature { get; set; }
    public int RainProbability { get; set; }
    public double Precipitation { get; set; }
    public double WindSpeed { get; set; }
    public double WindGusts { get; set; }
    public int WeatherCode { get; set; }
    public string WeatherDescription { get; set; } = "Ciel dégagé";
    public string WeatherEmoji { get; set; } = "☀️";
}

public class CommuteSlotSummary
{
    public string Title { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public double MinTemp { get; set; }
    public double MaxTemp { get; set; }
    public int MaxRainProbability { get; set; }
    public double TotalPrecipitation { get; set; }
    public double MaxWindSpeed { get; set; }
    public double MaxWindGusts { get; set; }
    public string MainCondition { get; set; } = string.Empty;
    public string MainEmoji { get; set; } = "⛅";
}

public class MorningBriefingSettings
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
    public DestinationType DestinationType { get; set; } = DestinationType.UserDm;
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

public class MorningBriefingResult
{
    public BikeVerdict Verdict { get; set; } = BikeVerdict.Ok;
    public string VerdictBanner { get; set; } = string.Empty;
    public string AllerSummary { get; set; } = string.Empty;
    public string RetourSummary { get; set; } = string.Empty;
    public string Advice { get; set; } = string.Empty;
}

public record DailyCommuteWeather(CommuteSlotSummary Aller, CommuteSlotSummary Retour);

public record WeatherCondition(string Description, string Emoji);

public record BriefingExecutionResult(bool Success, string Message, Embed? Embed = null);
