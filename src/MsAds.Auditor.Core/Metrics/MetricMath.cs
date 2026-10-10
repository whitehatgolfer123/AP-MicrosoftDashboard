namespace MsAds.Auditor.Core.Metrics;

/// <summary>
/// Arithmetic helpers that return <see langword="null"/> rather than dividing by zero or
/// producing an infinite percentage change. Every derived metric in the application funnels
/// through here so the zero-denominator behaviour is defined in exactly one place.
/// </summary>
public static class MetricMath
{
    /// <summary>
    /// Divides, returning <see langword="null"/> when the result is not defined: a zero
    /// denominator, or a missing numerator or denominator.
    /// </summary>
    public static decimal? Divide(decimal? numerator, decimal? denominator)
    {
        if (numerator is null || denominator is null || denominator.Value == 0m)
        {
            return null;
        }

        return numerator.Value / denominator.Value;
    }

    /// <summary>
    /// Divides and expresses the result as a percentage (0-100), or <see langword="null"/> when
    /// not defined.
    /// </summary>
    public static decimal? Rate(decimal? numerator, decimal? denominator)
        => Divide(numerator, denominator) * 100m;

    /// <summary>The simple difference, or <see langword="null"/> if either side is missing.</summary>
    public static decimal? AbsoluteChange(decimal? current, decimal? previous)
        => current is null || previous is null ? null : current.Value - previous.Value;

    /// <summary>
    /// Percentage change from <paramref name="previous"/> to <paramref name="current"/>.
    /// Returns <see langword="null"/> when either value is missing, or when the previous value is
    /// zero: growth from zero is not a meaningful percentage, and is described by
    /// <see cref="ChangeKind.NewActivity"/> instead.
    /// </summary>
    public static decimal? PercentChange(decimal? current, decimal? previous)
    {
        if (current is null || previous is null || previous.Value == 0m)
        {
            return null;
        }

        return (current.Value - previous.Value) / Math.Abs(previous.Value) * 100m;
    }

    /// <summary>
    /// Classifies the movement between two values, separating the cases that a percentage change
    /// cannot express.
    /// </summary>
    public static ChangeKind Classify(decimal? current, decimal? previous)
    {
        if (current is null || previous is null)
        {
            return ChangeKind.NotComparable;
        }

        return (current.Value, previous.Value) switch
        {
            (0m, 0m) => ChangeKind.NoChange,
            (> 0m, 0m) => ChangeKind.NewActivity,
            (0m, > 0m) => ChangeKind.CeasedActivity,
            var (c, p) when c > p => ChangeKind.Increase,
            var (c, p) when c < p => ChangeKind.Decrease,
            _ => ChangeKind.NoChange,
        };
    }

    /// <summary>
    /// Sums values, treating the whole result as unknown only when no input carries a value.
    /// Used for optional columns such as conversions, where a missing column must not be
    /// silently read as zero.
    /// </summary>
    public static decimal? SumOptional(IEnumerable<decimal?> values)
    {
        decimal total = 0m;
        var anyPresent = false;

        foreach (var value in values)
        {
            if (value.HasValue)
            {
                anyPresent = true;
                total += value.Value;
            }
        }

        return anyPresent ? total : null;
    }
}
