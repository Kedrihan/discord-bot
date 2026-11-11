namespace DiscordBot.DTO;

public class HourlyWeatherSlotDto
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
