namespace DiscordBot.Models;

public enum DestinationType
{
    UserDm,
    Channel
}

public class TechWatchSettings
{
    public bool IsEnabled { get; set; } = true;

    // Planification
    public List<DayOfWeek> ScheduleDays { get; set; } = new() { DayOfWeek.Monday };
    public TimeOnly ScheduleTime { get; set; } = new TimeOnly(7, 30);

    // Destinataire
    public DestinationType DestinationType { get; set; } = DestinationType.UserDm;
    public string TargetId { get; set; } = "116893981526196227";

    // Thématiques / Sujets
    public List<string> Topics { get; set; } = new()
    {
        "dotnet", "csharp", "vue", "javascript", "typescript",
        "postgresql", "architecture", "api", "testing", "security",
        "performance", "ai", "llm"
    };

    // Sources d'informations
    public bool EnableDevTo { get; set; } = true;
    public bool EnableHackerNews { get; set; } = true;
    public bool EnableDotnetBlogRss { get; set; } = true;
    public List<string> CustomRssFeeds { get; set; } = new();

    // IA & Formatage
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.0-flash";
    public int MaxArticles { get; set; } = 7;
    public bool TranslateToFrench { get; set; } = true;
}

public class NewsArticle
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}
