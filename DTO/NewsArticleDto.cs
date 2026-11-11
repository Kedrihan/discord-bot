namespace DiscordBot.DTO;

public class NewsArticleDto
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}
