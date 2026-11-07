using Upcheck.Api.Domain;
using Upcheck.Api.Monitoring;

namespace Upcheck.Api.Features;

/// <summary>Create/update payload for a monitor. Optional fields fall back to sensible defaults.</summary>
public sealed record MonitorInput(
    string Name,
    string Url,
    int? IntervalSeconds,
    int? TimeoutMs,
    int? ExpectedStatus,
    int? FailureThreshold,
    bool? Enabled);

public sealed record MonitorDto(
    int Id,
    string Name,
    string Url,
    int IntervalSeconds,
    int TimeoutMs,
    int ExpectedStatus,
    int FailureThreshold,
    bool Enabled,
    string Status,
    DateTimeOffset? LastCheckedAt,
    double Uptime24h);

public sealed record CheckDto(DateTimeOffset CheckedAt, bool Up, int? StatusCode, int ResponseMs, string? Error);

public sealed record IncidentDto(
    int Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt,
    bool IsOpen,
    string Cause,
    long? DurationSeconds);

public sealed record MonitorDetailDto(
    MonitorDto Monitor,
    UptimeStats Last24h,
    UptimeStats Last7d,
    IReadOnlyList<CheckDto> RecentChecks,
    IReadOnlyList<IncidentDto> Incidents);

public sealed record StatusSummaryDto(
    string Overall,
    int Total,
    int Up,
    int Down,
    int Pending,
    IReadOnlyList<MonitorDto> Monitors);

public static class Mapping
{
    public static MonitorDto ToDto(this Monitor m, double uptime24h) => new(
        m.Id, m.Name, m.Url, m.IntervalSeconds, m.TimeoutMs, m.ExpectedStatus, m.FailureThreshold,
        m.Enabled, m.Status.ToString(), m.LastCheckedAt, uptime24h);

    public static CheckDto ToDto(this Check c) => new(c.CheckedAt, c.Up, c.StatusCode, c.ResponseMs, c.Error);

    public static IncidentDto ToDto(this Incident i, DateTimeOffset now)
    {
        var end = i.ResolvedAt ?? now;
        var duration = (long)(end - i.StartedAt).TotalSeconds;
        return new IncidentDto(i.Id, i.StartedAt, i.ResolvedAt, i.IsOpen, i.Cause, duration);
    }
}
