using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Tests.Metrics;

/// <summary>
/// The zero-denominator and missing-value behaviour required by the brief: no division by zero,
/// and no infinite percentage change when the previous period has no data.
/// </summary>
public class MetricMathTests
{
    [Fact]
    public void Divide_returns_the_quotient_for_ordinary_values()
        => Assert.Equal(2.5m, MetricMath.Divide(10m, 4m));

    [Fact]
    public void Divide_returns_null_rather_than_throwing_when_the_denominator_is_zero()
        => Assert.Null(MetricMath.Divide(10m, 0m));

    [Theory]
    [InlineData(null, 4d)]
    [InlineData(10d, null)]
    [InlineData(null, null)]
    public void Divide_returns_null_when_either_side_is_missing(double? numerator, double? denominator)
        => Assert.Null(MetricMath.Divide((decimal?)numerator, (decimal?)denominator));

    [Fact]
    public void Rate_expresses_the_quotient_as_a_percentage()
        => Assert.Equal(25m, MetricMath.Rate(1m, 4m));

    [Fact]
    public void PercentChange_is_signed_relative_to_the_previous_value()
    {
        Assert.Equal(50m, MetricMath.PercentChange(150m, 100m));
        Assert.Equal(-25m, MetricMath.PercentChange(75m, 100m));
    }

    [Fact]
    public void PercentChange_returns_null_when_the_previous_value_is_zero()
    {
        // Growth from zero is not a percentage. Classify reports it as NewActivity instead.
        Assert.Null(MetricMath.PercentChange(50m, 0m));
        Assert.Equal(ChangeKind.NewActivity, MetricMath.Classify(50m, 0m));
    }

    [Fact]
    public void PercentChange_returns_null_when_either_value_is_missing()
    {
        Assert.Null(MetricMath.PercentChange(50m, null));
        Assert.Null(MetricMath.PercentChange(null, 50m));
    }

    [Fact]
    public void PercentChange_uses_the_magnitude_of_the_previous_value_so_the_sign_tracks_direction()
    {
        // A move from -10 to -5 is an increase, and must not be reported as a fall.
        Assert.Equal(50m, MetricMath.PercentChange(-5m, -10m));
    }

    [Theory]
    [InlineData(0, 0, ChangeKind.NoChange)]
    [InlineData(5, 0, ChangeKind.NewActivity)]
    [InlineData(0, 5, ChangeKind.CeasedActivity)]
    [InlineData(10, 5, ChangeKind.Increase)]
    [InlineData(5, 10, ChangeKind.Decrease)]
    [InlineData(5, 5, ChangeKind.NoChange)]
    public void Classify_separates_the_cases_a_percentage_cannot_express(
        double current, double previous, ChangeKind expected)
        => Assert.Equal(expected, MetricMath.Classify((decimal)current, (decimal)previous));

    [Fact]
    public void Classify_reports_not_comparable_when_a_value_is_missing()
        => Assert.Equal(ChangeKind.NotComparable, MetricMath.Classify(5m, null));

    [Fact]
    public void SumOptional_returns_null_only_when_no_input_carries_a_value()
    {
        Assert.Null(MetricMath.SumOptional([null, null]));
        Assert.Null(MetricMath.SumOptional([]));
        Assert.Equal(5m, MetricMath.SumOptional([null, 5m]));
        Assert.Equal(8m, MetricMath.SumOptional([3m, 5m]));
    }
}
