namespace DiscordBot.DTO.Embeds;

public record DiscordEmbedTemplateDto
{
    public int Id { get; init; }
    public EmbedTemplateTypeDto TemplateType { get; init; }
    public string GuildId { get; init; } = string.Empty;
    public string ChannelTargetId { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#9146FF";
    public string TitleTemplate { get; init; } = string.Empty;
    public string DescriptionTemplate { get; init; } = string.Empty;
    public bool EnableAuthor { get; init; } = true;
    public string AuthorNameTemplate { get; init; } = string.Empty;
    public string FooterTemplate { get; init; } = string.Empty;
    public bool ShowThumbnail { get; init; } = true;
    public bool ShowTimestamp { get; init; } = true;
    public List<DiscordEmbedFieldTemplateDto> CustomFields { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}
