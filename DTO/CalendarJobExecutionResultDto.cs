namespace DiscordBot.DTO;

public record CalendarJobExecutionResultDto(bool Success, string Message)
{
    public static CalendarJobExecutionResultDto Succeeded(string message = "Exécution réussie") => new(true, message);
    public static CalendarJobExecutionResultDto Failed(string message) => new(false, message);
}
