using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Upcheck.Api.Monitoring;
using Xunit;

namespace Upcheck.Api.Tests;

public sealed record MonitorRef(int Id, string Name, string Url, int IntervalSeconds, bool Enabled, string Status, double Uptime24h);
public sealed record CheckRef(DateTimeOffset CheckedAt, bool Up, int? StatusCode, int ResponseMs, string? Error);
public sealed record IncidentRef(int Id, DateTimeOffset StartedAt, DateTimeOffset? ResolvedAt, bool IsOpen, string Cause, long? DurationSeconds);
public sealed record UptimeRef(int Total, int UpCount, double UptimePercent, int? AverageResponseMs);
public sealed record DetailRef(MonitorRef Monitor, UptimeRef Last24h, UptimeRef Last7d, List<CheckRef> RecentChecks, List<IncidentRef> Incidents);
public sealed record StatusRef(string Overall, int Total, int Up, int Down, int Pending, List<MonitorRef> Monitors);

public sealed class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<MonitorRef> CreateAsync(string name = "Example", string url = "https://example.com", int? interval = null)
    {
        var resp = await _client.PostAsJsonAsync("/api/monitors", new { name, url, intervalSeconds = interval });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<MonitorRef>())!;
    }

    [Fact]
    public async Task Create_then_list_shows_a_pending_monitor()
    {
        var created = await CreateAsync("Homepage", "https://home.example");
        created.Status.Should().Be("Pending");

        var list = await _client.GetFromJsonAsync<List<MonitorRef>>("/api/monitors");
        list!.Should().Contain(m => m.Id == created.Id && m.Name == "Homepage");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    public async Task Invalid_url_is_rejected(string url)
    {
        var resp = await _client.PostAsJsonAsync("/api/monitors", new { name = "Bad", url });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Too_frequent_interval_is_rejected()
    {
        var resp = await _client.PostAsJsonAsync("/api/monitors", new { name = "Fast", url = "https://x.example", intervalSeconds = 1 });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_successful_check_marks_the_monitor_up()
    {
        _factory.Probe.Respond = _ => ProbeResult.Ok(200, 42);
        var m = await CreateAsync("Up site", "https://up.example");

        var resp = await _client.PostAsync($"/api/monitors/{m.Id}/check", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<MonitorRef>())!.Status.Should().Be("Up");

        var detail = await _client.GetFromJsonAsync<DetailRef>($"/api/monitors/{m.Id}");
        detail!.Last24h.Total.Should().Be(1);
        detail.Last24h.UptimePercent.Should().Be(100);
        detail.RecentChecks.Should().ContainSingle(c => c.Up && c.StatusCode == 200);
    }

    [Fact]
    public async Task Repeated_failures_open_an_incident_and_recovery_resolves_it()
    {
        var m = await CreateAsync("Flaky", "https://flaky.example"); // default threshold = 2

        _factory.Probe.Respond = _ => ProbeResult.Bad(500, 8, "server error");
        await _client.PostAsync($"/api/monitors/{m.Id}/check", null); // failure 1 -> still pending
        var afterTwo = await (await _client.PostAsync($"/api/monitors/{m.Id}/check", null)).Content.ReadFromJsonAsync<MonitorRef>();
        afterTwo!.Status.Should().Be("Down");

        var incidents = await _client.GetFromJsonAsync<List<IncidentRef>>($"/api/monitors/{m.Id}/incidents");
        incidents!.Should().ContainSingle(i => i.IsOpen && i.Cause == "server error");

        _factory.Probe.Respond = _ => ProbeResult.Ok(200, 10);
        var recovered = await (await _client.PostAsync($"/api/monitors/{m.Id}/check", null)).Content.ReadFromJsonAsync<MonitorRef>();
        recovered!.Status.Should().Be("Up");

        var resolved = await _client.GetFromJsonAsync<List<IncidentRef>>($"/api/monitors/{m.Id}/incidents");
        resolved!.Should().OnlyContain(i => !i.IsOpen && i.ResolvedAt != null);
    }

    [Fact]
    public async Task Status_summary_reports_degraded_when_a_monitor_is_down()
    {
        var m = await CreateAsync("Down site", "https://down.example");
        _factory.Probe.Respond = _ => ProbeResult.Bad(503, 5, "unavailable");
        await _client.PostAsync($"/api/monitors/{m.Id}/check", null);
        await _client.PostAsync($"/api/monitors/{m.Id}/check", null);

        var status = await _client.GetFromJsonAsync<StatusRef>("/api/status");
        status!.Down.Should().BeGreaterThanOrEqualTo(1);
        status.Overall.Should().Be("degraded");
    }

    [Fact]
    public async Task Status_page_renders_html()
    {
        await CreateAsync("Pagetest", "https://page.example");
        var resp = await _client.GetAsync("/");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Pagetest");
    }

    [Fact]
    public async Task Delete_removes_the_monitor()
    {
        var m = await CreateAsync("Temp", "https://temp.example");
        (await _client.DeleteAsync($"/api/monitors/{m.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/monitors/{m.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Check_now_on_missing_monitor_is_not_found()
    {
        (await _client.PostAsync("/api/monitors/99999/check", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public sealed class ApiKeyTests
{
    [Fact]
    public async Task Mutations_require_the_api_key_when_configured()
    {
        using var factory = new ApiFactory { ApiKey = "secret-key" };
        var client = factory.CreateClient();

        var noKey = await client.PostAsJsonAsync("/api/monitors", new { name = "X", url = "https://x.example" });
        noKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/monitors")
        {
            Content = JsonContent.Create(new { name = "X", url = "https://x.example" }),
        };
        req.Headers.Add("X-Api-Key", "secret-key");
        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Created);

        // reads stay public
        (await client.GetAsync("/api/monitors")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
