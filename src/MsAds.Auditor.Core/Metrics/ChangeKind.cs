namespace MsAds.Auditor.Core.Metrics;

/// <summary>
/// How a metric moved between two periods. The cases beyond simple increase and decrease exist
/// because a percentage change cannot describe them.
/// </summary>
public enum ChangeKind
{
    /// <summary>One or both values are missing, so no comparison can be drawn.</summary>
    NotComparable,

    NoChange,

    Increase,

    Decrease,

    /// <summary>Was zero, now above zero. No percentage change is defined.</summary>
    NewActivity,

    /// <summary>Was above zero, now zero. A -100% change, but worth naming separately.</summary>
    CeasedActivity,
}

/// <summary>
/// Whether a rise in a metric is desirable. Spend and CPA rising is not the same news as
/// conversion value rising, so the direction of "good" is a property of each metric.
/// </summary>
public enum MetricPolarity
{
    /// <summary>Neither direction is inherently good or bad, e.g. impressions, spend in isolation.</summary>
    Neutral,

    HigherIsBetter,

    LowerIsBetter,
}

/// <summary>The judgement shown to the user for a single metric movement.</summary>
public enum Verdict
{
    Unknown,
    Neutral,
    Positive,
    Negative,
}
