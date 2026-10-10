namespace MsAds.Auditor.Core.Reports;

/// <summary>
/// Which analyses the user asked for. Skipping an analysis avoids requesting the report behind
/// it, which matters because each Microsoft Advertising report is a separate asynchronous job.
/// </summary>
public sealed record AnalysisSelection
{
    public bool Campaigns { get; init; } = true;

    public bool SearchTerms { get; init; } = true;

    public bool Keywords { get; init; } = true;

    public bool ChangeHistory { get; init; } = true;

    public bool NegativeKeywordConflicts { get; init; } = true;

    public static AnalysisSelection All => new();

    public bool Any => Campaigns || SearchTerms || Keywords || ChangeHistory || NegativeKeywordConflicts;
}
