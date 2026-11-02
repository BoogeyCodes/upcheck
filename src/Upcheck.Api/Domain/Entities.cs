namespace Upcheck.Api.Domain;

/// <summary>Current health of a monitor.</summary>
public enum MonitorStatus
{
    /// <summary>No check has run yet.</summary>
    Pending,
    Up,
    Down,
}

/// <summary>An endpoint to watch: what to request and how often.</summary>
public sealed class Monitor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }

    /// <summary>How often to check, in seconds.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Per-request timeout, in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>HTTP status that counts as healthy. 0 means "any 2xx".</summary>
    public int ExpectedStatus { get; set; }

    /// <summary>Consecutive failures required before the monitor is marked Down.</summary>
    public int FailureThreshold { get; set; } = 2;

    public bool Enabled { get; set; } = true;

    public MonitorStatus Status { get; set; } = MonitorStatus.Pending;
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Check> Checks { get; set; } = new List<Check>();
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}

/// <summary>The recorded outcome of a single probe.</summary>
public sealed class Check
{
    public int Id { get; set; }
    public int MonitorId { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public bool Up { get; set; }
    public int? StatusCode { get; set; }
    public int ResponseMs { get; set; }
    public string? Error { get; set; }

    public Monitor? Monitor { get; set; }
}

/// <summary>A span of downtime: opened when a monitor goes Down, resolved when it recovers.</summary>
public sealed class Incident
{
    public int Id { get; set; }
    public int MonitorId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public required string Cause { get; set; }

    public bool IsOpen => ResolvedAt is null;

    public Monitor? Monitor { get; set; }
}
