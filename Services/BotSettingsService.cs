namespace DiscordBot.Services;

public class BotSettings
{
    // Espace réservé pour vos futurs paramètres
}

public class BotSettingsService
{
    public BotSettings Settings { get; private set; } = new();
    public DateTimeOffset StartTime { get; } = DateTimeOffset.UtcNow;

    public TimeSpan Uptime => DateTimeOffset.UtcNow - StartTime;

    public event Action? OnSettingsChanged;

    public void UpdateSettings(BotSettings newSettings)
    {
        Settings = newSettings;
        OnSettingsChanged?.Invoke();
    }
}
