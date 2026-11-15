namespace DiscordBot.DTO.YouTube;

public class TrackedYouTubeChannelDto
{
    public string GuildId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ChannelHandle { get; set; } = string.Empty;
    public string DiscordChannelId { get; set; } = string.Empty;
    public string CustomMessage { get; set; } = "ðŸŽ¥ Nouvelle vidÃ©o de **{channel}** !\n{url}";
    public bool IsEnabled { get; set; } = true;
}
