using Upcheck.Api.Domain;

namespace Upcheck.Api.Monitoring;

/// <summary>What a probe result did to a monitor's health.</summary>
public enum Transition
{
    /// <summary>Status unchanged (or still within the failure threshold).</summary>
    None,

    /// <summary>The monitor just crossed its failure threshold and went Down.</summary>
    WentDown,

    /// <summary>The monitor was Down and has now recovered.</summary>
    Recovered,
}

/// <summary>
/// The pure up/down state machine. Applying a <see cref="ProbeResult"/> updates the monitor's
/// status and failure counter in place and reports whether an incident should open or close.
/// A monitor only flips to Down after <see cref="Monitor.FailureThreshold"/> consecutive failures,
/// which absorbs one-off blips; a single success clears the counter and resolves any incident.
/// </summary>
public static class MonitorEvaluator
{
    public static Transition Apply(Monitor monitor, ProbeResult result, DateTimeOffset now)
    {
        monitor.LastCheckedAt = now;

        if (result.Up)
        {
            monitor.ConsecutiveFailures = 0;
            var recovered = monitor.Status == MonitorStatus.Down;
            monitor.Status = MonitorStatus.Up;
            return recovered ? Transition.Recovered : Transition.None;
        }

        monitor.ConsecutiveFailures++;
        if (monitor.Status != MonitorStatus.Down && monitor.ConsecutiveFailures >= monitor.FailureThreshold)
        {
            monitor.Status = MonitorStatus.Down;
            return Transition.WentDown;
        }

        return Transition.None;
    }
}
