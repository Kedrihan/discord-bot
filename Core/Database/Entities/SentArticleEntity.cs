namespace DiscordBot.Core.Database.Entities;

public class SentArticleEntity
{
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
}
