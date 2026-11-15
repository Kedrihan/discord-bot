namespace DiscordBot.DTO.Embeds;

public record DiscordEmbedFieldTemplateDto(
    string Name,
    string Value,
    bool Inline = true,
    bool IsEnabled = true,
    int Order = 0
);
