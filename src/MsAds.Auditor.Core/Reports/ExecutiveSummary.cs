namespace MsAds.Auditor.Core.Reports;

/// <summary>
/// A plain-English summary of the report, assembled from calculated figures only. No AI service
/// is involved: every sentence is generated from the numbers already shown elsewhere in the
/// report, so the text cannot contradict them.
/// </summary>
public sealed record ExecutiveSummary
{
    /// <summary>The single most important sentence.</summary>
    public required string Headline { get; init; }

    /// <summary>Supporting sentences, each derived from retrieved and calculated data.</summary>
    public IReadOnlyList<string> Points { get; init; } = [];

    /// <summary>Data-quality statements the reader needs in order to weigh the summary.</summary>
    public IReadOnlyList<string> Caveats { get; init; } = [];
}
