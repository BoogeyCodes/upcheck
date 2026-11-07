using System.Net;
using System.Text;

namespace Upcheck.Api.Features;

/// <summary>A small self-contained HTML status page rendered from the current monitor states.</summary>
public static class StatusPage
{
    public static void MapStatusPage(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", async (MonitorService s, CancellationToken ct) =>
                Results.Content(Render(await s.StatusAsync(ct)), "text/html"))
            .ExcludeFromDescription();
    }

    private static string Render(StatusSummaryDto status)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="refresh" content="15">
            <title>Upcheck — Status</title>
            <style>
            :root{color-scheme:dark}
            body{margin:0;background:#0f1419;color:#e6edf3;font:15px/1.5 system-ui,Segoe UI,Roboto,sans-serif}
            .wrap{max-width:760px;margin:0 auto;padding:32px 16px}
            h1{font-size:22px;margin:0 0 4px}.muted{color:#8b98a9}
            .banner{margin:18px 0;padding:14px 16px;border-radius:10px;font-weight:600}
            .operational{background:#113a2c;color:#3fb68b}.degraded{background:#3a1b1b;color:#e5736b}
            .pending{background:#2a2f18;color:#d8c15a}
            .m{display:flex;align-items:center;justify-content:space-between;gap:12px;
               padding:14px;border:1px solid #2a3542;border-radius:10px;margin-bottom:10px;background:#171d26}
            .name{font-weight:600}.url{font-size:12px;color:#8b98a9;word-break:break-all}
            .right{text-align:right;white-space:nowrap}
            .badge{display:inline-block;padding:3px 10px;border-radius:999px;font-size:12px;font-weight:600}
            .up{background:#113a2c;color:#3fb68b}.down{background:#3a1b1b;color:#e5736b}.pend{background:#2a2f18;color:#d8c15a}
            .pct{font-variant-numeric:tabular-nums;font-size:13px;color:#8b98a9}
            footer{margin-top:24px;font-size:12px;color:#8b98a9}
            </style></head><body><div class="wrap">
            """);

        sb.Append("<h1>Upcheck</h1><p class=\"muted\">Service status</p>");
        var bannerClass = status.Overall switch { "operational" => "operational", "degraded" => "degraded", _ => "pending" };
        var bannerText = status.Overall switch
        {
            "operational" => "All systems operational",
            "degraded" => $"{status.Down} of {status.Total} monitors down",
            _ => "Waiting for first checks",
        };
        sb.Append($"<div class=\"banner {bannerClass}\">{E(bannerText)}</div>");

        if (status.Monitors.Count == 0)
        {
            sb.Append("<p class=\"muted\">No monitors configured yet.</p>");
        }

        foreach (var m in status.Monitors)
        {
            var (badgeClass, badgeText) = m.Status switch
            {
                "Up" => ("up", "operational"),
                "Down" => ("down", "down"),
                _ => ("pend", "pending"),
            };
            var last = m.LastCheckedAt is { } t ? t.UtcDateTime.ToString("u") : "never";
            sb.Append($"""
                <div class="m"><div><div class="name">{E(m.Name)}</div><div class="url">{E(m.Url)}</div></div>
                <div class="right"><span class="badge {badgeClass}">{badgeText}</span>
                <div class="pct">{m.Uptime24h:0.##}% · 24h · checked {E(last)}</div></div></div>
                """);
        }

        sb.Append($"<footer>Auto-refreshing every 15s · {status.Up} up / {status.Down} down / {status.Pending} pending</footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
