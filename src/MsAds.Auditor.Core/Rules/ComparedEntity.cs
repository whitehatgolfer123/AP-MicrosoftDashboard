using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Core.Rules;

/// <summary>
/// An entity's current and previous period metrics side by side. Campaigns and keywords are both
/// projected onto this shape so that rules such as "spend up, conversions down" can be written
/// once and applied to either.
/// </summary>
public sealed record ComparedEntity
{
    public required string EntityType { get; init; }

    public required long EntityId { get; init; }

    public required string Name { get; init; }

    public string? Status { get; init; }

    /// <summary>Where the entity sits in the account hierarchy, for display.</summary>
    public string? Context { get; init; }

    public required MetricSet Current { get; init; }

    /// <summary>Null when the entity had no data in the previous period at all.</summary>
    public MetricSet? Previous { get; init; }

    public decimal? ImpressionShare { get; init; }

    public decimal? ImpressionShareLostToBudget { get; init; }

    public decimal? ImpressionShareLostToRank { get; init; }

    public decimal? QualityScore { get; init; }

    public bool HasComparison => Previous is not null;

    public MetricDelta Delta(MetricKey key) => MetricDelta.Create(
        Current.Get(key),
        Previous?.Get(key),
        MetricCatalogue.Describe(key).Polarity);

    /// <summary>
    /// Whether this entity carries enough spend and clicks for its performance to be judged
    /// rather than merely described.
    /// </summary>
    public bool MeetsEvidenceBar(decimal minimumSpend, int minimumClicks)
        => Current.Spend >= minimumSpend && Current.Clicks >= minimumClicks;
}
