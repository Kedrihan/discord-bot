namespace DiscordBot.Services;

public class BotSettings
{
    // Espace réservé pour de futurs paramètres
}

public class BotSettingsService(TimeProvider timeProvider)
{
    public BotSettings Settings { get; private set; } = new();
    public DateTimeOffset StartTime { get; } = timeProvider.GetUtcNow();

    public TimeSpan Uptime => timeProvider.GetUtcNow() - StartTime;

    public event Action? OnSettingsChanged;

    public void UpdateSettings(BotSettings newSettings)
    {
        Settings = newSettings;
        OnSettingsChanged?.Invoke();
    }
}
