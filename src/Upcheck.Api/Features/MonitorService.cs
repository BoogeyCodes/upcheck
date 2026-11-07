using Microsoft.EntityFrameworkCore;
using Upcheck.Api.Common;
using Upcheck.Api.Data;
using Upcheck.Api.Domain;
using Upcheck.Api.Monitoring;

namespace Upcheck.Api.Features;

/// <summary>Everything the API does with monitors: CRUD, stats, running a check, the status summary.</summary>
public sealed class MonitorService(AppDbContext db, IClock clock, MonitorRunner runner)
{
    public async Task<IReadOnlyList<MonitorDto>> ListAsync(CancellationToken ct)
    {
        var monitors = await db.Monitors.OrderBy(m => m.Id).ToListAsync(ct);
        var since = clock.UtcNow.AddDays(-1);
        var checks = await db.Checks.Where(c => c.CheckedAt >= since).ToListAsync(ct);
        var byMonitor = checks.GroupBy(c => c.MonitorId).ToDictionary(g => g.Key, g => g.ToList());

        return monitors
            .Select(m => m.ToDto(Uptime24h(byMonitor.GetValueOrDefault(m.Id), since)))
            .ToList();
    }

    public async Task<MonitorDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == id, ct)
                      ?? throw new NotFoundException($"Monitor {id} was not found.");

        var now = clock.UtcNow;
        var dayAgo = now.AddDays(-1);
        var weekAgo = now.AddDays(-7);

        var checks = await db.Checks
            .Where(c => c.MonitorId == id && c.CheckedAt >= weekAgo)
            .OrderByDescending(c => c.CheckedAt)
            .ToListAsync(ct);

        var incidents = await db.Incidents
            .Where(i => i.MonitorId == id)
            .OrderByDescending(i => i.StartedAt)
            .Take(50)
            .ToListAsync(ct);

        var last24 = UptimeCalculator.Compute(checks, dayAgo);
        var last7 = UptimeCalculator.Compute(checks, weekAgo);

        return new MonitorDetailDto(
            monitor.ToDto(last24.UptimePercent),
            last24,
            last7,
            checks.Take(100).Select(c => c.ToDto()).ToList(),
            incidents.Select(i => i.ToDto(now)).ToList());
    }

    public async Task<MonitorDto> CreateAsync(MonitorInput input, CancellationToken ct)
    {
        var monitor = new Monitor
        {
            Name = Require(input.Name, "name"),
            Url = ValidUrl(input.Url),
            IntervalSeconds = Clamp(input.IntervalSeconds ?? 60, 5, 86_400, "intervalSeconds"),
            TimeoutMs = Clamp(input.TimeoutMs ?? 5000, 100, 60_000, "timeoutMs"),
            ExpectedStatus = ValidStatus(input.ExpectedStatus ?? 0),
            FailureThreshold = Clamp(input.FailureThreshold ?? 2, 1, 10, "failureThreshold"),
            Enabled = input.Enabled ?? true,
            CreatedAt = clock.UtcNow,
        };
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync(ct);
        return monitor.ToDto(0);
    }

    public async Task<MonitorDto> UpdateAsync(int id, MonitorInput input, CancellationToken ct)
    {
        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == id, ct)
                      ?? throw new NotFoundException($"Monitor {id} was not found.");

        monitor.Name = Require(input.Name, "name");
        monitor.Url = ValidUrl(input.Url);
        monitor.IntervalSeconds = Clamp(input.IntervalSeconds ?? monitor.IntervalSeconds, 5, 86_400, "intervalSeconds");
        monitor.TimeoutMs = Clamp(input.TimeoutMs ?? monitor.TimeoutMs, 100, 60_000, "timeoutMs");
        monitor.ExpectedStatus = ValidStatus(input.ExpectedStatus ?? monitor.ExpectedStatus);
        monitor.FailureThreshold = Clamp(input.FailureThreshold ?? monitor.FailureThreshold, 1, 10, "failureThreshold");
        monitor.Enabled = input.Enabled ?? monitor.Enabled;
        await db.SaveChangesAsync(ct);
        return monitor.ToDto(0);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.Monitors.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0)
        {
            throw new NotFoundException($"Monitor {id} was not found.");
        }
    }

    public async Task<MonitorDto> CheckNowAsync(int id, CancellationToken ct)
    {
        var monitor = await db.Monitors.FirstOrDefaultAsync(m => m.Id == id, ct)
                      ?? throw new NotFoundException($"Monitor {id} was not found.");

        await runner.RunMonitorAsync(monitor, ct);
        await db.SaveChangesAsync(ct);
        return monitor.ToDto(0);
    }

    public async Task<StatusSummaryDto> StatusAsync(CancellationToken ct)
    {
        var dtos = await ListAsync(ct);
        var up = dtos.Count(d => d.Status == nameof(MonitorStatus.Up));
        var down = dtos.Count(d => d.Status == nameof(MonitorStatus.Down));
        var pending = dtos.Count(d => d.Status == nameof(MonitorStatus.Pending));
        var overall = down > 0 ? "degraded" : pending == dtos.Count && dtos.Count > 0 ? "pending" : "operational";
        return new StatusSummaryDto(overall, dtos.Count, up, down, pending, dtos);
    }

    private static double Uptime24h(List<Check>? checks, DateTimeOffset since) =>
        checks is null ? 0 : UptimeCalculator.Compute(checks, since).UptimePercent;

    // --- validation helpers ---

    private static string Require(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ValidationException($"{field} is required.");
        }

        return trimmed;
    }

    private static string ValidUrl(string? url)
    {
        var value = Require(url, "url");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ValidationException("url must be an absolute http(s) URL.");
        }

        return value;
    }

    private static int Clamp(int value, int min, int max, string field)
    {
        if (value < min || value > max)
        {
            throw new ValidationException($"{field} must be between {min} and {max}.");
        }

        return value;
    }

    private static int ValidStatus(int status)
    {
        if (status != 0 && status is < 100 or > 599)
        {
            throw new ValidationException("expectedStatus must be 0 (any 2xx) or a valid HTTP status code.");
        }

        return status;
    }
}
