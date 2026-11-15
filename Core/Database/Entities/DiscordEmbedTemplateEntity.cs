namespace DiscordBot.Core.Database.Entities;

public class DiscordEmbedTemplateEntity
{
    public int Id { get; set; }
    public string TemplateType { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string ChannelTargetId { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#9146FF";
    public string TitleTemplate { get; set; } = string.Empty;
    public string DescriptionTemplate { get; set; } = string.Empty;
    public bool EnableAuthor { get; set; } = true;
    public string AuthorNameTemplate { get; set; } = string.Empty;
    public string FooterTemplate { get; set; } = string.Empty;
    public bool ShowThumbnail { get; set; } = true;
    public bool ShowTimestamp { get; set; } = true;
    public string CustomFieldsJson { get; set; } = "[]";
    public long UpdatedAt { get; set; }
}
