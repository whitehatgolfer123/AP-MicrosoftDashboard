using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.Reports;

/// <summary>
/// A completed audit. Self-contained: it carries the dates, the settings and the data-quality
/// notices that produced it, so the report can be displayed, exported or re-read later without
/// needing to reconstruct the context.
/// </summary>
public sealed record AuditReport
{
    public required Guid ReportId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required AdAccountInfo Account { get; init; }

    public required ResolvedPeriod Period { get; init; }

    public required AuditSettings Settings { get; init; }

    public required AnalysisSelection Selection { get; init; }

    /// <summary>Whether this report came from the live API or from demo data.</summary>
    public required DataSourceKind SourceKind { get; init; }

    public required string SourceName { get; init; }

    public bool IsDemo => SourceKind == DataSourceKind.Demo;

    public required MetricSet Current { get; init; }

    public MetricSet? Previous { get; init; }

    /// <summary>The summary cards, in display order, each with its change and verdict.</summary>
    public IReadOnlyList<MetricComparison> Summary { get; init; } = [];

    public IReadOnlyList<DailyMetrics> DailyCurrent { get; init; } = [];

    public IReadOnlyList<DailyMetrics> DailyPrevious { get; init; } = [];

    public IReadOnlyList<ComparedEntity> Campaigns { get; init; } = [];

    public IReadOnlyList<ComparedEntity> Keywords { get; init; } = [];

    public required SearchTermAnalysis SearchTerms { get; init; }

    public IReadOnlyList<ChangeRecord> Changes { get; init; } = [];

    public IReadOnlyList<NegativeKeywordConflict> NegativeKeywordConflicts { get; init; } = [];

    public required RecommendationResult Recommendations { get; init; }

    public required ExecutiveSummary ExecutiveSummary { get; init; }

    /// <summary>Every data-quality notice gathered while building the report, de-duplicated.</summary>
    public IReadOnlyList<DataNotice> Notices { get; init; } = [];

    /// <summary>Formatter matching the account's currency and culture.</summary>
    public required MetricFormatter Formatter { get; init; }

    public bool ConversionDataUnavailable { get; init; }

    public bool ChangeHistoryUnavailable { get; init; }

    /// <summary>How long the retrieval and analysis took, for the interface to display.</summary>
    public TimeSpan Duration { get; init; }

    public IEnumerable<DataNotice> Warnings
        => Notices.Where(n => n.Severity is NoticeSeverity.Warning or NoticeSeverity.Error);

    public string DateRangeLabel => $"{Formatter.FormatDate(Period.Current.Start)} – {Formatter.FormatDate(Period.Current.End)}";

    public string ComparisonLabel => Period.Previous is { } previous
        ? $"{Formatter.FormatDate(previous.Start)} – {Formatter.FormatDate(previous.End)}"
        : "No comparison";
}
