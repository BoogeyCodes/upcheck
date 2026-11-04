using Upcheck.Api.Domain;

namespace Upcheck.Api.Monitoring;

/// <summary>The system clock, abstracted so the scheduler and tests can control time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>The outcome of probing a monitor's URL once.</summary>
public sealed record ProbeResult(bool Up, int? StatusCode, int ResponseMs, string? Error)
{
    public static ProbeResult Ok(int statusCode, int responseMs) => new(true, statusCode, responseMs, null);
    public static ProbeResult Bad(int? statusCode, int responseMs, string error) => new(false, statusCode, responseMs, error);
}

/// <summary>Performs the actual check against a monitor's endpoint.</summary>
public interface IProbe
{
    Task<ProbeResult> ProbeAsync(Monitor monitor, CancellationToken ct);
}
