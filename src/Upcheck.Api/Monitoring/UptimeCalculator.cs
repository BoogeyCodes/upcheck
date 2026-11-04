using Upcheck.Api.Domain;

namespace Upcheck.Api.Monitoring;

/// <summary>Uptime figures over a window: how many checks ran, how many were up, and the averages.</summary>
public sealed record UptimeStats(int Total, int UpCount, double UptimePercent, int? AverageResponseMs);

public static class UptimeCalculator
{
    /// <summary>Compute uptime over the checks at or after <paramref name="since"/>.</summary>
    public static UptimeStats Compute(IEnumerable<Check> checks, DateTimeOffset since)
    {
        var window = checks.Where(c => c.CheckedAt >= since).ToList();
        if (window.Count == 0)
        {
            return new UptimeStats(0, 0, 0, null);
        }

        var up = window.Count(c => c.Up);
        var percent = Math.Round(up * 100.0 / window.Count, 2);
        var averageMs = (int)Math.Round(window.Average(c => c.ResponseMs));
        return new UptimeStats(window.Count, up, percent, averageMs);
    }
}
