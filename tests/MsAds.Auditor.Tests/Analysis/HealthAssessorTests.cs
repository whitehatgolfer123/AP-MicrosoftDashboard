using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Settings;

namespace MsAds.Auditor.Tests.Analysis;

/// <summary>
/// The direction-of-travel verdict used by the portfolio scan. Judged on efficiency rather than
/// on spend, so an account spending more is not thereby better or worse.
/// </summary>
public class HealthAssessorTests
{
    private static readonly AuditSettings Settings = new() { SignificantChangePercent = 20m };
    private static readonly MetricFormatter Formatter = new("en-GB", "GBP");

    private static (HealthVerdict Verdict, string Reason, bool BasedOnConversions) Assess(
        MetricSet current, MetricSet? previous)
        => HealthAssessor.Assess(current, previous, Settings, Formatter);

    private static MetricSet Metrics(decimal spend, long clicks, decimal? conversions)
        => new() { Impressions = clicks * 20, Clicks = clicks, Spend = spend, Conversions = conversions };

    [Fact]
    public void An_account_that_stopped_delivering_is_the_headline_state()
    {
        var (verdict, reason, _) = Assess(MetricSet.Empty, Metrics(500m, 200, 10m));

        Assert.Equal(HealthVerdict.NotDelivering, verdict);
        Assert.Contains("nothing in this one", reason);
    }

    [Fact]
    public void An_account_with_no_delivery_in_either_period_is_dormant_not_broken()
        => Assert.Equal(HealthVerdict.Dormant, Assess(MetricSet.Empty, MetricSet.Empty).Verdict);

    [Fact]
    public void A_worsening_cpa_beyond_the_threshold_is_declining()
    {
        // CPA from £25 to £50.
        var (verdict, _, basedOnConversions) = Assess(Metrics(500m, 200, 10m), Metrics(500m, 200, 20m));

        Assert.Equal(HealthVerdict.Declining, verdict);
        Assert.True(basedOnConversions);
    }

    [Fact]
    public void An_improving_cpa_beyond_the_threshold_is_improving()
    {
        // CPA from £50 to £25.
        Assert.Equal(HealthVerdict.Improving, Assess(Metrics(500m, 200, 20m), Metrics(500m, 200, 10m)).Verdict);
    }

    [Fact]
    public void A_cpa_movement_inside_the_threshold_is_stable()
    {
        // CPA from £50.00 to £52.63 — about 5%.
        Assert.Equal(HealthVerdict.Stable, Assess(Metrics(500m, 200, 9.5m), Metrics(500m, 200, 10m)).Verdict);
    }

    [Fact]
    public void Spend_rising_on_its_own_does_not_make_an_account_worse()
    {
        // Spend doubles but conversions double too, so efficiency is unchanged.
        Assert.Equal(HealthVerdict.Stable, Assess(Metrics(1000m, 400, 20m), Metrics(500m, 200, 10m)).Verdict);
    }

    [Fact]
    public void Spending_with_no_conversions_at_all_outranks_any_percentage_movement()
    {
        var (verdict, reason, _) = Assess(Metrics(500m, 200, 0m), Metrics(500m, 200, 10m));

        Assert.Equal(HealthVerdict.Declining, verdict);
        Assert.Contains("no recorded conversions", reason);
    }

    [Fact]
    public void Without_conversion_data_the_verdict_falls_back_to_cost_of_traffic_and_says_so()
    {
        // CPC from £2.00 to £3.00 with no conversion columns at all.
        var (verdict, reason, basedOnConversions) =
            Assess(Metrics(600m, 200, null), Metrics(400m, 200, null));

        Assert.Equal(HealthVerdict.Declining, verdict);
        Assert.False(basedOnConversions);
        Assert.Contains("No conversion data", reason);
    }

    [Fact]
    public void Without_a_comparison_period_no_direction_is_claimed()
    {
        var (verdict, reason, _) = Assess(Metrics(500m, 200, 10m), null);

        Assert.Equal(HealthVerdict.Unknown, verdict);
        Assert.Contains("no comparison period", reason);
    }

    [Fact]
    public void Accounts_needing_attention_sort_above_healthy_ones()
    {
        var order = new[]
        {
            HealthVerdict.Improving, HealthVerdict.NotDelivering, HealthVerdict.Stable,
            HealthVerdict.Declining, HealthVerdict.Dormant,
        }
        .Select(v => new AccountHealth
        {
            Account = Core.Demo.DemoAccounts.Primary,
            Current = MetricSet.Empty,
            Verdict = v,
            Reason = "",
            BasedOnConversions = true,
        })
        .OrderBy(a => a.SortWeight)
        .Select(a => a.Verdict)
        .ToList();

        Assert.Equal(HealthVerdict.NotDelivering, order[0]);
        Assert.Equal(HealthVerdict.Declining, order[1]);
        Assert.Equal(HealthVerdict.Dormant, order[^1]);
    }

    [Fact]
    public void The_key_metrics_shown_on_a_scan_row_are_spend_clicks_conversions_and_cpa()
    {
        var keys = HealthAssessor.KeyMetrics(Metrics(500m, 200, 10m), Metrics(400m, 180, 9m))
            .Select(m => m.Key)
            .ToList();

        Assert.Equal(
            [MetricKey.Spend, MetricKey.Clicks, MetricKey.Conversions, MetricKey.CostPerAcquisition],
            keys);
    }
}
