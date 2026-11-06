namespace DiscordBot.Models;

public enum TargetProtocol
{
    Http,
    Tcp
}

public enum TargetStatus
{
    Pending,
    Up,
    Down
}

public class MonitoredTarget
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public TargetProtocol Protocol { get; set; } = TargetProtocol.Http;
    public string HostOrUrl { get; set; } = string.Empty;
    public int? Port { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
    public int FailureThreshold { get; set; } = 2;
    public bool IsEnabled { get; set; } = true;
    public string? AlertDiscordChannelId { get; set; }
}

public class UptimeSettings
{
    public bool IsEnabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 60;
    public string DefaultAlertChannelId { get; set; } = string.Empty;
    public List<MonitoredTarget> Targets { get; set; } = [];
}

public class UptimeTargetState
{
    public string TargetId { get; set; } = string.Empty;
    public TargetStatus Status { get; set; } = TargetStatus.Pending;
    public int ConsecutiveFailures { get; set; }
    public long LastResponseTimeMs { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? LastStatusChangeAt { get; set; }
    public DateTimeOffset? DownSince { get; set; }
    public string? LastErrorMessage { get; set; }
}

public class UptimeState
{
    public List<UptimeTargetState> TargetStates { get; set; } = [];
}

public record SystemMetrics(
    TimeSpan Uptime,
    DateTimeOffset StartTime,
    long WorkingSetBytes,
    long GcTotalBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ThreadCount,
    string DotNetVersion,
    string OsDescription,
    string Architecture
);

public record ServiceHealthInfo(
    string Name,
    bool IsHealthy,
    string StatusText,
    string? Details = null
);

public record TargetCheckResult(
    bool Success,
    long ResponseTimeMs,
    string? ErrorMessage = null
);

public record UptimeExecutionResult(
    bool Success,
    string Message,
    int TargetsCheckedCount = 0
);
