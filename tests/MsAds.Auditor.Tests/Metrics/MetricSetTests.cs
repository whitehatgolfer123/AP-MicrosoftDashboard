using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Tests.Metrics;

public class MetricSetTests
{
    [Fact]
    public void Derived_metrics_are_calculated_from_the_retrieved_counters()
    {
        var metrics = new MetricSet
        {
            Impressions = 10_000,
            Clicks = 400,
            Spend = 800m,
            Conversions = 20m,
            ConversionValue = 4000m,
        };

        Assert.Equal(4m, metrics.ClickThroughRate);
        Assert.Equal(2m, metrics.AverageCpc);
        Assert.Equal(5m, metrics.ConversionRate);
        Assert.Equal(40m, metrics.CostPerAcquisition);
        Assert.Equal(5m, metrics.ReturnOnAdSpend);
        Assert.Equal(200m, metrics.ValuePerConversion);
    }

    [Fact]
    public void Ctr_is_null_rather_than_zero_when_there_were_no_impressions()
        => Assert.Null(new MetricSet { Impressions = 0, Clicks = 0 }.ClickThroughRate);

    [Fact]
    public void Cpa_is_null_when_there_were_no_conversions_so_spend_is_never_shown_as_infinite_cost()
    {
        var metrics = new MetricSet { Clicks = 100, Spend = 500m, Conversions = 0m };

        Assert.Null(metrics.CostPerAcquisition);
        Assert.True(metrics.HasSpendButNoConversions);
    }

    [Fact]
    public void Missing_conversion_data_is_distinct_from_a_recorded_zero()
    {
        var missing = new MetricSet { Clicks = 100, Spend = 500m, Conversions = null };
        var zero = new MetricSet { Clicks = 100, Spend = 500m, Conversions = 0m };

        Assert.False(missing.HasConversionData);
        Assert.False(missing.HasSpendButNoConversions);

        Assert.True(zero.HasConversionData);
        Assert.True(zero.HasSpendButNoConversions);
    }

    [Fact]
    public void Roas_is_null_when_conversion_value_was_not_reported()
        => Assert.Null(new MetricSet { Spend = 100m, ConversionValue = null }.ReturnOnAdSpend);

    [Fact]
    public void Sum_totals_the_counters()
    {
        var total = MetricSet.Sum([
            new MetricSet { Impressions = 100, Clicks = 10, Spend = 20m, Conversions = 1m },
            new MetricSet { Impressions = 200, Clicks = 20, Spend = 30m, Conversions = 2m },
        ]);

        Assert.Equal(300, total.Impressions);
        Assert.Equal(30, total.Clicks);
        Assert.Equal(50m, total.Spend);
        Assert.Equal(3m, total.Conversions);
    }

    [Fact]
    public void Sum_keeps_conversions_unknown_when_no_row_reported_them()
    {
        var total = MetricSet.Sum([
            new MetricSet { Clicks = 10, Spend = 20m, Conversions = null },
            new MetricSet { Clicks = 20, Spend = 30m, Conversions = null },
        ]);

        Assert.Null(total.Conversions);
        Assert.False(total.HasConversionData);
    }

    [Fact]
    public void Sum_of_an_empty_sequence_reports_no_conversion_data_rather_than_zero()
        => Assert.Null(MetricSet.Sum([]).Conversions);

    [Fact]
    public void Compare_produces_one_comparison_per_dashboard_card_in_display_order()
    {
        var comparisons = MetricSet.Compare(
            new MetricSet { Spend = 120m, Clicks = 10, Conversions = 2m },
            new MetricSet { Spend = 100m, Clicks = 10, Conversions = 2m });

        Assert.Equal(MetricCatalogue.DashboardOrder.Count, comparisons.Count);
        Assert.Equal(MetricCatalogue.DashboardOrder, [.. comparisons.Select(c => c.Key)]);

        var spend = comparisons.First(c => c.Key == MetricKey.Spend);
        Assert.Equal(20m, spend.Delta.PercentChange);
    }

    [Fact]
    public void Compare_against_no_previous_period_yields_comparisons_that_are_not_comparable()
    {
        var comparisons = MetricSet.Compare(new MetricSet { Spend = 100m }, previous: null);

        Assert.All(comparisons, c => Assert.Equal(ChangeKind.NotComparable, c.Delta.Kind));
        Assert.All(comparisons, c => Assert.Equal(Verdict.Unknown, c.Delta.Verdict));
    }
}
