using DiscordBot.DTO.Common;

namespace DiscordBot.DTO.TechWatch;

public class TechWatchSettingsDto
{
    public bool IsEnabled { get; set; } = true;

    // Planification
    public List<DayOfWeek> ScheduleDays { get; set; } = [DayOfWeek.Monday];
    public TimeOnly ScheduleTime { get; set; } = new(7, 30);

    // Destinataire
    public DestinationTypeDto DestinationType { get; set; } = DestinationTypeDto.UserDm;
    public string TargetId { get; set; } = string.Empty;

    // Thématiques / Sujets
    public List<string> Topics { get; set; } =
    [
        "dotnet", "csharp", "vue", "javascript", "typescript",
        "postgresql", "architecture", "api", "testing", "security",
        "performance", "ai", "llm"
    ];

    // Sources d'informations
    public bool EnableDevTo { get; set; } = true;
    public bool EnableHackerNews { get; set; } = true;
    public bool EnableDotnetBlogRss { get; set; } = true;
    public List<string> CustomRssFeeds { get; set; } = [];

    // IA & Formatage
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public int MaxArticles { get; set; } = 7;
    public bool TranslateToFrench { get; set; } = true;
}
