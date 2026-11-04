using Microsoft.EntityFrameworkCore;
using Upcheck.Api.Data;
using Upcheck.Api.Domain;

namespace Upcheck.Api.Monitoring;

/// <summary>
/// Runs the checks that are due: probes each monitor, records the result, drives the state
/// machine and opens/closes incidents. This is the schedulable unit of work; the background
/// service just calls <see cref="RunDueAsync"/> on a loop, and tests call it directly.
/// </summary>
public sealed class MonitorRunner(AppDbContext db, IProbe probe, IClock clock, ILogger<MonitorRunner> logger)
{
    /// <summary>Whether a monitor should be checked at <paramref name="now"/>.</summary>
    public static bool IsDue(Monitor monitor, DateTimeOffset now) =>
        monitor.Enabled &&
        (monitor.LastCheckedAt is null ||
         now - monitor.LastCheckedAt.Value >= TimeSpan.FromSeconds(monitor.IntervalSeconds));

    /// <summary>Run every monitor that is currently due. Returns how many ran.</summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var monitors = await db.Monitors.Where(m => m.Enabled).ToListAsync(ct);
        var due = monitors.Where(m => IsDue(m, now)).ToList();

        foreach (var monitor in due)
        {
            await RunMonitorAsync(monitor, ct);
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return due.Count;
    }

    /// <summary>Probe a single monitor and apply the result (does not call SaveChanges).</summary>
    public async Task RunMonitorAsync(Monitor monitor, CancellationToken ct)
    {
        var result = await probe.ProbeAsync(monitor, ct);
        var now = clock.UtcNow;

        db.Checks.Add(new Check
        {
            MonitorId = monitor.Id,
            CheckedAt = now,
            Up = result.Up,
            StatusCode = result.StatusCode,
            ResponseMs = result.ResponseMs,
            Error = result.Error,
        });

        switch (MonitorEvaluator.Apply(monitor, result, now))
        {
            case Transition.WentDown:
                db.Incidents.Add(new Incident
                {
                    MonitorId = monitor.Id,
                    StartedAt = now,
                    Cause = result.Error ?? "check failed",
                });
                logger.LogWarning("Monitor {Monitor} went DOWN: {Cause}", monitor.Name, result.Error);
                break;

            case Transition.Recovered:
                var open = await db.Incidents
                    .Where(i => i.MonitorId == monitor.Id && i.ResolvedAt == null)
                    .ToListAsync(ct);
                foreach (var incident in open)
                {
                    incident.ResolvedAt = now;
                }
                logger.LogInformation("Monitor {Monitor} recovered", monitor.Name);
                break;
        }
    }
}
