using System.Diagnostics;
using Upcheck.Api.Domain;

namespace Upcheck.Api.Monitoring;

/// <summary>Probes a monitor with a real HTTP GET, honouring its timeout and expected status.</summary>
public sealed class HttpProbe(IHttpClientFactory httpClientFactory) : IProbe
{
    public async Task<ProbeResult> ProbeAsync(Monitor monitor, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("probe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(monitor.TimeoutMs);

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, monitor.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            sw.Stop();

            var code = (int)response.StatusCode;
            return IsExpected(monitor, code)
                ? ProbeResult.Ok(code, (int)sw.ElapsedMilliseconds)
                : ProbeResult.Bad(code, (int)sw.ElapsedMilliseconds, $"unexpected status {code}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return ProbeResult.Bad(null, (int)sw.ElapsedMilliseconds, $"timed out after {monitor.TimeoutMs} ms");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return ProbeResult.Bad(null, (int)sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private static bool IsExpected(Monitor monitor, int statusCode) =>
        monitor.ExpectedStatus == 0 ? statusCode is >= 200 and < 300 : statusCode == monitor.ExpectedStatus;
}
