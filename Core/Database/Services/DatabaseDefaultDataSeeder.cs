using DiscordBot.Core.Database.Entities;
using DiscordBot.DTO;

namespace DiscordBot.Core.Database.Services;

public static class DatabaseDefaultDataSeeder
{
    public static MorningBriefingSettingsEntity CreateDefaultMorningBriefingSettings() => new()
    {
        Id = 1,
        IsEnabled = true,
        ScheduleDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday],
        ScheduleTime = new TimeOnly(7, 0),
        DestinationType = DestinationTypeDto.UserDm,
        TargetId = "116893981526196227",
        Address = "19 Impasse de Vérone, 69150 Décines-Charpieu",
        Latitude = 45.7678,
        Longitude = 4.9619,
        MorningStartHour = 7,
        MorningEndHour = 8,
        EveningStartHour = 17,
        EveningEndHour = 18
    };

    public static TechWatchSettingsEntity CreateDefaultTechWatchSettings() => new()
    {
        Id = 1,
        IsEnabled = true,
        ScheduleDays = [DayOfWeek.Monday],
        ScheduleTime = new TimeOnly(7, 30),
        DestinationType = DestinationTypeDto.UserDm,
        TargetId = "116893981526196227",
        Topics =
        [
            "dotnet", "csharp", "vue", "javascript", "typescript",
            "postgresql", "architecture", "api", "testing", "security",
            "performance", "ai", "llm"
        ],
        EnableDevTo = true,
        EnableHackerNews = true,
        EnableDotnetBlogRss = true,
        CustomRssFeeds = [],
        GeminiModel = "gemini-flash-latest",
        MaxArticles = 7,
        TranslateToFrench = true
    };

    public static TwitchSettingsEntity CreateDefaultTwitchSettings() => new()
    {
        Id = 1,
        IsEnabled = true,
        IsClipsEnabled = true,
        CheckIntervalSeconds = 30,
        ClipCheckIntervalSeconds = 60,
        Channels = []
    };

    public static YouTubeSettingsEntity CreateDefaultYouTubeSettings() => new()
    {
        Id = 1,
        IsEnabled = true,
        CheckIntervalMinutes = 15,
        Channels = []
    };
}
