using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.Rules;

/// <summary>
/// Everything the rules can read. Assembled once per report so that rules are pure functions of
/// their input and therefore straightforward to test.
/// </summary>
public sealed record AuditContext
{
    public required ReportScope Scope { get; init; }

    public required ResolvedPeriod Period { get; init; }

    public required AuditSettings Settings { get; init; }

    public required MetricFormatter Formatter { get; init; }

    public required MetricSet AccountCurrent { get; init; }

    public MetricSet? AccountPrevious { get; init; }

    public IReadOnlyList<ComparedEntity> Campaigns { get; init; } = [];

    public IReadOnlyList<ComparedEntity> Keywords { get; init; } = [];

    public IReadOnlyList<SearchTermPerformance> SearchTerms { get; init; } = [];

    public IReadOnlyList<ChangeRecord> Changes { get; init; } = [];

    public IReadOnlyList<NegativeKeywordConflict> NegativeKeywordConflicts { get; init; } = [];

    public IReadOnlyList<DailyMetrics> DailyCurrent { get; init; } = [];

    public IReadOnlyList<DailyMetrics> DailyPrevious { get; init; } = [];

    /// <summary>True when conversion data was unavailable, so conversion-based rules must stand down.</summary>
    public bool ConversionDataUnavailable { get; init; }

    public bool SearchTermDataUnavailable { get; init; }

    public bool ChangeHistoryUnavailable { get; init; }

    /// <summary>The account's average CPA, used to compare campaigns against the account as a whole.</summary>
    public decimal? AccountCpa => AccountCurrent.CostPerAcquisition;

    public decimal? AccountCtr => AccountCurrent.ClickThroughRate;
}

/// <summary>
/// A deterministic rule. Rules must not call out to any service; they read the context and
/// return findings, which is what makes the thresholds testable.
/// </summary>
public interface IAuditRule
{
    string RuleId { get; }

    string Name { get; }

    /// <summary>What this rule looks for, shown in the settings screen.</summary>
    string Description { get; }

    IEnumerable<Finding> Evaluate(AuditContext context);
}
