using Discord;

namespace DiscordBot.DTO.MorningBriefing;

public record BriefingExecutionResultDto(bool Success, string Message, Embed? Embed = null);
