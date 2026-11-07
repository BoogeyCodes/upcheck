using Microsoft.EntityFrameworkCore;
using Upcheck.Api.Common;
using Upcheck.Api.Data;
using Upcheck.Api.Monitoring;

namespace Upcheck.Api.Features;

public static class MonitorEndpoints
{
    public static void MapMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api").WithTags("Monitors");

        read.MapGet("/monitors", async (MonitorService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(ct)));

        read.MapGet("/monitors/{id:int}", async (int id, MonitorService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(id, ct)));

        read.MapGet("/monitors/{id:int}/checks", async (int id, int? limit, AppDbContext db, IClock clock, CancellationToken ct) =>
        {
            await EnsureExists(db, id, ct);
            var take = Math.Clamp(limit ?? 50, 1, 500);
            var checks = await db.Checks
                .Where(c => c.MonitorId == id)
                .OrderByDescending(c => c.CheckedAt)
                .Take(take)
                .ToListAsync(ct);
            return Results.Ok(checks.Select(c => c.ToDto()).ToList());
        });

        read.MapGet("/monitors/{id:int}/incidents", async (int id, AppDbContext db, IClock clock, CancellationToken ct) =>
        {
            await EnsureExists(db, id, ct);
            var now = clock.UtcNow;
            var incidents = await db.Incidents
                .Where(i => i.MonitorId == id)
                .OrderByDescending(i => i.StartedAt)
                .ToListAsync(ct);
            return Results.Ok(incidents.Select(i => i.ToDto(now)).ToList());
        });

        read.MapGet("/status", async (MonitorService s, CancellationToken ct) =>
            Results.Ok(await s.StatusAsync(ct)));

        var write = app.MapGroup("/api").WithTags("Monitors").AddEndpointFilter<ApiKeyFilter>();

        write.MapPost("/monitors", async (MonitorInput body, MonitorService s, CancellationToken ct) =>
        {
            var dto = await s.CreateAsync(body, ct);
            return Results.Created($"/api/monitors/{dto.Id}", dto);
        });

        write.MapPut("/monitors/{id:int}", async (int id, MonitorInput body, MonitorService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(id, body, ct)));

        write.MapDelete("/monitors/{id:int}", async (int id, MonitorService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        write.MapPost("/monitors/{id:int}/check", async (int id, MonitorService s, CancellationToken ct) =>
            Results.Ok(await s.CheckNowAsync(id, ct)));
    }

    private static async Task EnsureExists(AppDbContext db, int id, CancellationToken ct)
    {
        if (!await db.Monitors.AnyAsync(m => m.Id == id, ct))
        {
            throw new NotFoundException($"Monitor {id} was not found.");
        }
    }
}
