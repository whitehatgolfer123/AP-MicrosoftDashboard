using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Core.Rules;

public enum FindingSeverity
{
    /// <summary>Something is working well and may be worth building on.</summary>
    Positive,

    /// <summary>Neutral context worth knowing.</summary>
    Information,

    /// <summary>Worth investigating.</summary>
    Warning,

    /// <summary>Likely to be costing money now.</summary>
    Critical,
}

/// <summary>
/// How much weight to put on a finding, driven by how much data supports it. A campaign with
/// three clicks and no conversions produces a low-confidence finding, not a verdict.
/// </summary>
public enum FindingConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>A single supporting number behind a finding.</summary>
public sealed record Evidence(string Label, string Value, string? Previous = null, string? Change = null);

/// <summary>
/// One observation about the account. Every finding carries the five things the brief requires:
/// the finding itself, the supporting data, why it was flagged, what to do next, and any caveats.
/// </summary>
public sealed record Finding
{
    /// <summary>Stable identifier of the rule that produced this, for testing and traceability.</summary>
    public required string RuleId { get; init; }

    public required string Title { get; init; }

    public required FindingSeverity Severity { get; init; }

    public required FindingConfidence Confidence { get; init; }

    /// <summary>The kind of thing this is about, e.g. Account, Campaign, Keyword, Search term.</summary>
    public required string EntityType { get; init; }

    public string? EntityName { get; init; }

    public long? EntityId { get; init; }

    /// <summary>Where the entity sits, e.g. "Brand – Exact &gt; Core terms".</summary>
    public string? EntityContext { get; init; }

    /// <summary>Why this was flagged, in plain English, referring to the supporting numbers.</summary>
    public required string Reason { get; init; }

    /// <summary>What the user should consider doing. Never an automatic change.</summary>
    public required string SuggestedAction { get; init; }

    /// <summary>Data-quality limits and reasons the finding might not hold.</summary>
    public IReadOnlyList<string> Caveats { get; init; } = [];

    public IReadOnlyList<Evidence> Evidence { get; init; } = [];

    /// <summary>Grouping for the interface, e.g. "Wasted spend", "Efficiency", "Delivery".</summary>
    public required string Category { get; init; }

    /// <summary>
    /// Spend associated with the finding, used only to rank findings so the most financially
    /// material appear first.
    /// </summary>
    public decimal? SpendAtStake { get; init; }

    public string DisplayName => EntityName ?? EntityType;
}

public static class FindingCategories
{
    public const string WastedSpend = "Wasted spend";
    public const string Efficiency = "Efficiency";
    public const string Delivery = "Delivery";
    public const string Relevance = "Relevance";
    public const string Opportunity = "Opportunity";
    public const string Tracking = "Tracking";
    public const string Changes = "Changes";
}
