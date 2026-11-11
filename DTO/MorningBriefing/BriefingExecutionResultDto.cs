using Discord;

namespace DiscordBot.DTO;

public record BriefingExecutionResultDto(bool Success, string Message, Embed? Embed = null);
