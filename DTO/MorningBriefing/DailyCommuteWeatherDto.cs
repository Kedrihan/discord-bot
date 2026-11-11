namespace DiscordBot.DTO;

public record DailyCommuteWeatherDto(CommuteSlotSummaryDto Aller, CommuteSlotSummaryDto Retour);
