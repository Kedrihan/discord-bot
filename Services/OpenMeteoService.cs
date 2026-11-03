using System.Globalization;
using System.Text.Json;
using DiscordBot.Models;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Services;

public class OpenMeteoService(HttpClient httpClient, ILogger<OpenMeteoService> logger)
{
    public async Task<DailyCommuteWeather?> GetCommuteWeatherAsync(MorningBriefingSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var latStr = settings.Latitude.ToString("F4", CultureInfo.InvariantCulture);
            var lonStr = settings.Longitude.ToString("F4", CultureInfo.InvariantCulture);

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&hourly=temperature_2m,precipitation_probability,precipitation,wind_speed_10m,wind_gusts_10m,weather_code&timezone=Europe%2FParis&forecast_days=1";

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Open-Meteo a renvoyé le statut {StatusCode}", response.StatusCode);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("hourly", out var hourly))
            {
                logger.LogError("Réponse Open-Meteo invalide : propriété 'hourly' absente");
                return null;
            }

            var times = hourly.GetProperty("time").EnumerateArray().Select(t => t.GetString()).ToList();
            var temps = hourly.GetProperty("temperature_2m").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var rainProbs = hourly.GetProperty("precipitation_probability").EnumerateArray().Select(t => t.GetInt32()).ToList();
            var precipitations = hourly.GetProperty("precipitation").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var windSpeeds = hourly.GetProperty("wind_speed_10m").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var windGusts = hourly.GetProperty("wind_gusts_10m").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var weatherCodes = hourly.GetProperty("weather_code").EnumerateArray().Select(t => t.GetInt32()).ToList();

            List<HourlyWeatherSlot> slots = [];
            for (var i = 0; i < times.Count; i++)
            {
                if (!DateTime.TryParse(times[i], out var dt))
                {
                    continue;
                }

                var condition = GetWeatherDescription(weatherCodes[i]);
                slots.Add(new HourlyWeatherSlot
                {
                    Hour = dt.Hour,
                    Temperature = temps[i],
                    RainProbability = rainProbs[i],
                    Precipitation = precipitations[i],
                    WindSpeed = windSpeeds[i],
                    WindGusts = windGusts[i],
                    WeatherCode = weatherCodes[i],
                    WeatherDescription = condition.Description,
                    WeatherEmoji = condition.Emoji
                });
            }

            var allerSlots = slots.Where(s => s.Hour >= settings.MorningStartHour && s.Hour <= settings.MorningEndHour).ToList();
            var retourSlots = slots.Where(s => s.Hour >= settings.EveningStartHour && s.Hour <= settings.EveningEndHour).ToList();

            var allerSummary = BuildSummary("Trajet Aller", $"{settings.MorningStartHour:D2}h - {settings.MorningEndHour:D2}h", allerSlots);
            var retourSummary = BuildSummary("Trajet Retour", $"{settings.EveningStartHour:D2}h - {settings.EveningEndHour:D2}h", retourSlots);

            return new DailyCommuteWeather(allerSummary, retourSummary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la récupération des prévisions météo Open-Meteo");
            return null;
        }
    }

    private static CommuteSlotSummary BuildSummary(string title, string timeRange, List<HourlyWeatherSlot> slots)
    {
        if (slots.Count == 0)
        {
            return new CommuteSlotSummary { Title = title, TimeRange = timeRange, MainCondition = "Données indisponibles" };
        }

        var mostSevereSlot = slots
            .OrderByDescending(s => s.Precipitation)
            .ThenByDescending(s => s.WindGusts)
            .First();

        return new CommuteSlotSummary
        {
            Title = title,
            TimeRange = timeRange,
            MinTemp = slots.Min(s => s.Temperature),
            MaxTemp = slots.Max(s => s.Temperature),
            MaxRainProbability = slots.Max(s => s.RainProbability),
            TotalPrecipitation = slots.Sum(s => s.Precipitation),
            MaxWindSpeed = slots.Max(s => s.WindSpeed),
            MaxWindGusts = slots.Max(s => s.WindGusts),
            MainCondition = mostSevereSlot.WeatherDescription,
            MainEmoji = mostSevereSlot.WeatherEmoji
        };
    }

    public static WeatherCondition GetWeatherDescription(int code) => code switch
    {
        0 => new WeatherCondition("Ciel dégagé", "☀️"),
        1 => new WeatherCondition("Principalement dégagé", "🌤️"),
        2 => new WeatherCondition("Éclaircies", "⛅"),
        3 => new WeatherCondition("Couvert", "☁️"),
        45 or 48 => new WeatherCondition("Brouillard", "🌫️"),
        51 or 53 or 55 => new WeatherCondition("Bruine", "🌦️"),
        61 => new WeatherCondition("Pluie faible", "🌧️"),
        63 => new WeatherCondition("Pluie modérée", "🌧️"),
        65 => new WeatherCondition("Pluie forte", "🌧️"),
        71 or 73 or 75 => new WeatherCondition("Chutes de neige", "🌨️"),
        80 or 81 or 82 => new WeatherCondition("Averses de pluie", "🌦️"),
        95 or 96 or 99 => new WeatherCondition("Orage", "⛈️"),
        _ => new WeatherCondition("Variable", "⛅")
    };
}
