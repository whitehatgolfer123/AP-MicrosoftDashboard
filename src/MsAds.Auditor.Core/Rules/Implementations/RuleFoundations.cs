using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Core.Rules.Implementations;

/// <summary>Which collection of entities a rule examines.</summary>
public enum EntityScope
{
    Campaign,
    Keyword,
}

/// <summary>
/// Base class for rules that apply equally to campaigns and keywords. Registering the same rule
/// twice, once per scope, avoids writing the same threshold logic in two places.
/// </summary>
public abstract class EntityRule(EntityScope scope) : IAuditRule
{
    protected EntityScope Scope { get; } = scope;

    protected string ScopeSlug => Scope == EntityScope.Campaign ? "campaign" : "keyword";

    protected string ScopeLabel => Scope == EntityScope.Campaign ? "Campaign" : "Keyword";

    protected string ScopeLabelLower => Scope == EntityScope.Campaign ? "campaign" : "keyword";

    protected IReadOnlyList<ComparedEntity> Entities(AuditContext context)
        => Scope == EntityScope.Campaign ? context.Campaigns : context.Keywords;

    public abstract string RuleId { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract IEnumerable<Finding> Evaluate(AuditContext context);
}

/// <summary>Shared helpers for turning metrics into the evidence rows shown under a finding.</summary>
public static class RuleEvidence
{
    /// <summary>One evidence row for a metric, including the previous value and change where known.</summary>
    public static Evidence For(MetricFormatter formatter, ComparedEntity entity, MetricKey key)
    {
        var delta = entity.Delta(key);

        return new Evidence(
            MetricCatalogue.Describe(key).ShortName,
            formatter.Format(delta.Current, key),
            delta.Previous is null ? null : formatter.Format(delta.Previous, key),
            delta.HasComparison ? formatter.FormatChange(delta) : null);
    }

    public static IReadOnlyList<Evidence> For(
        MetricFormatter formatter, ComparedEntity entity, params MetricKey[] keys)
        => [.. keys.Select(key => For(formatter, entity, key))];

    /// <summary>
    /// Grades confidence by how much traffic underpins the finding, so thin data produces a
    /// cautious finding instead of a confident wrong one.
    /// </summary>
    public static FindingConfidence ConfidenceFromVolume(long clicks, int minimumClicks)
        => clicks >= minimumClicks * 2 ? FindingConfidence.High
            : clicks >= minimumClicks ? FindingConfidence.Medium
            : FindingConfidence.Low;

    /// <summary>The caveat added whenever a finding rests on less traffic than the configured bar.</summary>
    public static string? ThinDataCaveat(long clicks, int minimumClicks, int periodDays)
        => clicks >= minimumClicks
            ? null
            : $"Only {clicks} click(s) over {periodDays} days, below the configured minimum of "
              + $"{minimumClicks}. This is not enough traffic to draw a firm conclusion.";

    public static string AttributionCaveat()
        => "Conversion figures depend on the account's selected conversion goals and attribution "
           + "settings, and can continue to accrue against recent dates.";
}
