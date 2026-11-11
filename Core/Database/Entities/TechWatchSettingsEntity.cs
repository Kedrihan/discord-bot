using DiscordBot.DTO;

namespace DiscordBot.Core.Database.Entities;

public class TechWatchSettingsEntity
{
    public int Id { get; set; }
    public bool IsEnabled { get; set; }
    public List<DayOfWeek> ScheduleDays { get; set; } = [];
    public TimeOnly ScheduleTime { get; set; }
    public DestinationTypeDto DestinationType { get; set; }
    public string TargetId { get; set; } = string.Empty;
    public List<string> Topics { get; set; } = [];
    public bool EnableDevTo { get; set; }
    public bool EnableHackerNews { get; set; }
    public bool EnableDotnetBlogRss { get; set; }
    public List<string> CustomRssFeeds { get; set; } = [];
    public string GeminiModel { get; set; } = string.Empty;
    public int MaxArticles { get; set; }
    public bool TranslateToFrench { get; set; }
}
