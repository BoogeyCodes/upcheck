using Microsoft.EntityFrameworkCore;
using Serilog;
using Upcheck.Api.Common;
using Upcheck.Api.Data;
using Upcheck.Api.Features;
using Upcheck.Api.Monitoring;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=upcheck.db"));

builder.Services.AddHttpClient("probe", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Upcheck/1.0");
    client.Timeout = Timeout.InfiniteTimeSpan; // per-monitor timeout is enforced in HttpProbe
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IProbe, HttpProbe>();
builder.Services.AddScoped<MonitorRunner>();
builder.Services.AddScoped<MonitorService>();

// The background scheduler is on by default; tests and read-only deployments can disable it.
if (builder.Configuration.GetValue("Scheduler:Enabled", true))
{
    builder.Services.AddHostedService<SchedulerWorker>();
}

builder.Services.AddExceptionHandler<ExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Upcheck API", Version = "v1" });
    options.CustomSchemaIds(t => t.DeclaringType is null ? t.Name : t.DeclaringType.Name + t.Name);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
}

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Health").ExcludeFromDescription();
app.MapStatusPage();
app.MapMonitorEndpoints();

app.Run();

// Exposes the implicit Program class to the integration test project.
public partial class Program;
