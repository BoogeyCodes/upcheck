using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Upcheck.Api.Domain;
using Upcheck.Api.Monitoring;

namespace Upcheck.Api.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>A controllable probe: set <see cref="Respond"/> to script each monitor's result.</summary>
public sealed class ScriptedProbe : IProbe
{
    public Func<Monitor, ProbeResult> Respond { get; set; } = _ => ProbeResult.Ok(200, 12);

    public Task<ProbeResult> ProbeAsync(Monitor monitor, CancellationToken ct) => Task.FromResult(Respond(monitor));
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"upcheck-tests-{Guid.NewGuid():N}.db");

    public FakeClock Clock { get; } = new();
    public ScriptedProbe Probe { get; } = new();

    /// <summary>When set, the app requires this API key on mutating endpoints.</summary>
    public string? ApiKey { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Scheduler:Enabled", "false"); // no background checks during tests
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        if (ApiKey is not null)
        {
            builder.UseSetting("ApiKey", ApiKey);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.RemoveAll<IProbe>();
            services.AddSingleton<IProbe>(Probe);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_dbPath))
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
    }
}
