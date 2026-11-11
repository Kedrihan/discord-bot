namespace DiscordBot.DTO;

public class MorningBriefingResultDto
{
    public BikeVerdictDto Verdict { get; set; } = BikeVerdictDto.Ok;
    public string VerdictBanner { get; set; } = string.Empty;
    public string AllerSummary { get; set; } = string.Empty;
    public string RetourSummary { get; set; } = string.Empty;
    public string Advice { get; set; } = string.Empty;
}
