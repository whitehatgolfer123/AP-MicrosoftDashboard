namespace MsAds.Auditor.Core.Metrics;

/// <summary>
/// A metric's current value alongside the previous period's, with the change expressed both
/// absolutely and proportionally, and a polarity-aware judgement.
/// </summary>
public sealed record MetricDelta
{
    public required decimal? Current { get; init; }

    public required decimal? Previous { get; init; }

    public decimal? Absolute { get; init; }

    /// <summary>
    /// Percentage change, or <see langword="null"/> when it is not meaningful. Check
    /// <see cref="Kind"/> before presenting this as the whole story.
    /// </summary>
    public decimal? PercentChange { get; init; }

    public required ChangeKind Kind { get; init; }

    public required MetricPolarity Polarity { get; init; }

    public required Verdict Verdict { get; init; }

    public bool HasComparison => Kind is not ChangeKind.NotComparable;

    public static MetricDelta Create(decimal? current, decimal? previous, MetricPolarity polarity)
    {
        var kind = MetricMath.Classify(current, previous);

        return new MetricDelta
        {
            Current = current,
            Previous = previous,
            Absolute = MetricMath.AbsoluteChange(current, previous),
            PercentChange = MetricMath.PercentChange(current, previous),
            Kind = kind,
            Polarity = polarity,
            Verdict = Judge(kind, polarity),
        };
    }

    private static Verdict Judge(ChangeKind kind, MetricPolarity polarity)
    {
        if (kind is ChangeKind.NotComparable)
        {
            return Verdict.Unknown;
        }

        if (kind is ChangeKind.NoChange || polarity is MetricPolarity.Neutral)
        {
            return Verdict.Neutral;
        }

        var roseInValue = kind is ChangeKind.Increase or ChangeKind.NewActivity;

        return polarity is MetricPolarity.HigherIsBetter
            ? roseInValue ? Verdict.Positive : Verdict.Negative
            : roseInValue ? Verdict.Negative : Verdict.Positive;
    }
}
