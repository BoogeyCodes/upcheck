using FluentAssertions;
using Upcheck.Api.Domain;
using Upcheck.Api.Monitoring;
using Xunit;

namespace Upcheck.Api.Tests;

public sealed class MonitorEvaluatorTests
{
    private static Monitor NewMonitor(int threshold = 2) => new()
    {
        Name = "m",
        Url = "https://example.com",
        FailureThreshold = threshold,
        Status = MonitorStatus.Pending,
    };

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_success_marks_up_without_an_incident()
    {
        var m = NewMonitor();
        var transition = MonitorEvaluator.Apply(m, ProbeResult.Ok(200, 10), Now);

        transition.Should().Be(Transition.None);
        m.Status.Should().Be(MonitorStatus.Up);
        m.ConsecutiveFailures.Should().Be(0);
        m.LastCheckedAt.Should().Be(Now);
    }

    [Fact]
    public void Goes_down_only_after_the_failure_threshold()
    {
        var m = NewMonitor(threshold: 2);

        MonitorEvaluator.Apply(m, ProbeResult.Bad(500, 5, "boom"), Now).Should().Be(Transition.None);
        m.Status.Should().Be(MonitorStatus.Pending); // one failure isn't enough
        m.ConsecutiveFailures.Should().Be(1);

        MonitorEvaluator.Apply(m, ProbeResult.Bad(500, 5, "boom"), Now).Should().Be(Transition.WentDown);
        m.Status.Should().Be(MonitorStatus.Down);
        m.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void A_success_clears_failures_and_recovers_from_down()
    {
        var m = NewMonitor(threshold: 1);
        MonitorEvaluator.Apply(m, ProbeResult.Bad(null, 5, "down"), Now).Should().Be(Transition.WentDown);
        m.Status.Should().Be(MonitorStatus.Down);

        MonitorEvaluator.Apply(m, ProbeResult.Ok(200, 8), Now).Should().Be(Transition.Recovered);
        m.Status.Should().Be(MonitorStatus.Up);
        m.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void A_single_blip_below_threshold_does_not_flap()
    {
        var m = NewMonitor(threshold: 3);
        m.Status = MonitorStatus.Up;

        MonitorEvaluator.Apply(m, ProbeResult.Bad(503, 5, "blip"), Now).Should().Be(Transition.None);
        MonitorEvaluator.Apply(m, ProbeResult.Ok(200, 9), Now).Should().Be(Transition.None);
        m.Status.Should().Be(MonitorStatus.Up);
    }
}

public sealed class UptimeCalculatorTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Check Check(bool up, int ms, int minuteOffset) => new()
    {
        Up = up,
        ResponseMs = ms,
        CheckedAt = Base.AddMinutes(minuteOffset),
    };

    [Fact]
    public void Empty_window_is_zero()
    {
        var stats = UptimeCalculator.Compute([], Base);
        stats.Total.Should().Be(0);
        stats.UptimePercent.Should().Be(0);
        stats.AverageResponseMs.Should().BeNull();
    }

    [Fact]
    public void Computes_percentage_and_average()
    {
        var checks = new[] { Check(true, 100, 1), Check(true, 200, 2), Check(false, 300, 3), Check(true, 400, 4) };
        var stats = UptimeCalculator.Compute(checks, Base);

        stats.Total.Should().Be(4);
        stats.UpCount.Should().Be(3);
        stats.UptimePercent.Should().Be(75);
        stats.AverageResponseMs.Should().Be(250);
    }

    [Fact]
    public void Only_counts_checks_in_the_window()
    {
        var checks = new[] { Check(false, 10, -120), Check(true, 10, 1) }; // first is before the window
        var stats = UptimeCalculator.Compute(checks, Base);
        stats.Total.Should().Be(1);
        stats.UptimePercent.Should().Be(100);
    }
}

public sealed class DueTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Never_checked_monitor_is_due()
    {
        var m = new Monitor { Name = "m", Url = "x", IntervalSeconds = 60, LastCheckedAt = null };
        MonitorRunner.IsDue(m, Now).Should().BeTrue();
    }

    [Fact]
    public void Within_interval_is_not_due_but_past_it_is()
    {
        var m = new Monitor { Name = "m", Url = "x", IntervalSeconds = 60, LastCheckedAt = Now.AddSeconds(-30) };
        MonitorRunner.IsDue(m, Now).Should().BeFalse();

        m.LastCheckedAt = Now.AddSeconds(-61);
        MonitorRunner.IsDue(m, Now).Should().BeTrue();
    }

    [Fact]
    public void Disabled_monitor_is_never_due()
    {
        var m = new Monitor { Name = "m", Url = "x", IntervalSeconds = 60, Enabled = false, LastCheckedAt = null };
        MonitorRunner.IsDue(m, Now).Should().BeFalse();
    }
}
