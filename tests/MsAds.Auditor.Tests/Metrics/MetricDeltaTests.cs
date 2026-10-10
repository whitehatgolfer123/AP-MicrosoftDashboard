using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Tests.Metrics;

/// <summary>
/// The brief requires that an increase is not assumed to be good. These tests pin the polarity
/// behaviour for the metrics where it matters most.
/// </summary>
public class MetricDeltaTests
{
    [Fact]
    public void A_rise_in_a_lower_is_better_metric_is_judged_negative()
    {
        var delta = MetricDelta.Create(60m, 40m, MetricPolarity.LowerIsBetter);

        Assert.Equal(ChangeKind.Increase, delta.Kind);
        Assert.Equal(Verdict.Negative, delta.Verdict);
        Assert.Equal(50m, delta.PercentChange);
        Assert.Equal(20m, delta.Absolute);
    }

    [Fact]
    public void A_rise_in_a_higher_is_better_metric_is_judged_positive()
        => Assert.Equal(Verdict.Positive, MetricDelta.Create(60m, 40m, MetricPolarity.HigherIsBetter).Verdict);

    [Fact]
    public void A_rise_in_a_neutral_metric_is_judged_neutral()
        => Assert.Equal(Verdict.Neutral, MetricDelta.Create(60m, 40m, MetricPolarity.Neutral).Verdict);

    [Fact]
    public void Spend_is_neutral_so_an_increase_is_not_presented_as_good_news()
        => Assert.Equal(MetricPolarity.Neutral, MetricCatalogue.Describe(MetricKey.Spend).Polarity);

    [Fact]
    public void Cpa_is_lower_is_better_and_conversion_value_is_higher_is_better()
    {
        Assert.Equal(MetricPolarity.LowerIsBetter, MetricCatalogue.Describe(MetricKey.CostPerAcquisition).Polarity);
        Assert.Equal(MetricPolarity.HigherIsBetter, MetricCatalogue.Describe(MetricKey.ConversionValue).Polarity);
    }

    [Fact]
    public void New_activity_is_judged_by_polarity_but_carries_no_percentage()
    {
        var delta = MetricDelta.Create(10m, 0m, MetricPolarity.HigherIsBetter);

        Assert.Equal(ChangeKind.NewActivity, delta.Kind);
        Assert.Null(delta.PercentChange);
        Assert.Equal(Verdict.Positive, delta.Verdict);
    }

    [Fact]
    public void Ceased_activity_in_a_higher_is_better_metric_is_negative()
    {
        var delta = MetricDelta.Create(0m, 10m, MetricPolarity.HigherIsBetter);

        Assert.Equal(ChangeKind.CeasedActivity, delta.Kind);
        Assert.Equal(Verdict.Negative, delta.Verdict);
    }

    [Fact]
    public void A_missing_value_yields_an_unknown_verdict_rather_than_a_neutral_one()
    {
        var delta = MetricDelta.Create(null, 10m, MetricPolarity.HigherIsBetter);

        Assert.Equal(Verdict.Unknown, delta.Verdict);
        Assert.False(delta.HasComparison);
    }
}
