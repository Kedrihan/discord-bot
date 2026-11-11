namespace DiscordBot.DTO;

public class CommuteSlotSummaryDto
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
