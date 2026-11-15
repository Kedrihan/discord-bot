namespace DiscordBot.DTO.MorningBriefing;

public record DailyCommuteWeatherDto(CommuteSlotSummaryDto Aller, CommuteSlotSummaryDto Retour);
