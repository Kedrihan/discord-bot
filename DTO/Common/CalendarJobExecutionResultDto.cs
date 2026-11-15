namespace DiscordBot.DTO.Common;

public record CalendarJobExecutionResultDto(bool Success, string Message)
{
    public static CalendarJobExecutionResultDto Succeeded(string message = "ExÃ©cution rÃ©ussie") => new(true, message);
    public static CalendarJobExecutionResultDto Failed(string message) => new(false, message);
}
