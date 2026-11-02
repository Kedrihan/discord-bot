namespace DiscordBot.Services;

public class BotSettings
{
    public string BotStatusMessage { get; set; } = "En cours d'exécution sur Oracle Cloud";
    public string DefaultPrefix { get; set; } = "!";
    public bool EnableWelcomeMessages { get; set; } = true;
    public string WelcomeChannel { get; set; } = "général";
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
