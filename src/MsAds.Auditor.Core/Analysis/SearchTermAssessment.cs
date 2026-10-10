using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Rules;

namespace MsAds.Auditor.Core.Analysis;

/// <summary>One search term, judged against the configured rules.</summary>
public sealed record SearchTermAssessment
{
    public required SearchTermPerformance Term { get; init; }

    public IReadOnlyList<SearchTermFlag> Flags { get; init; } = [];

    public required SearchTermAction RecommendedAction { get; init; }

    /// <summary>Why this term was flagged, referring to its own numbers.</summary>
    public required string Reason { get; init; }

    public required FindingConfidence Confidence { get; init; }

    public IReadOnlyList<string> Caveats { get; init; } = [];

    /// <summary>
    /// Spend on this term that is not currently producing conversions. An estimate of exposure,
    /// not a promise of savings: excluding the term does not necessarily recover the money.
    /// </summary>
    public decimal UnproductiveSpend { get; init; }

    /// <summary>
    /// True when the evidence supports proposing this as a negative keyword. Deliberately
    /// stricter than merely being flagged.
    /// </summary>
    public bool IsNegativeKeywordCandidate { get; init; }

    public bool IsFlagged => Flags.Count > 0 && !(Flags.Count == 1 && Flags[0] == SearchTermFlag.InsufficientEvidence);

    public string FlagSummary => string.Join(", ", Flags.Select(f => f.Label()));
}

/// <summary>The outcome of analysing an account's search terms for one period.</summary>
public sealed record SearchTermAnalysis
{
    public IReadOnlyList<SearchTermAssessment> Assessments { get; init; } = [];

    public IReadOnlyList<DataNotice> Notices { get; init; } = [];

    /// <summary>
    /// True when search term data could not be retrieved at all. Distinct from retrieving it and
    /// finding nothing to flag.
    /// </summary>
    public bool DataUnavailable { get; init; }

    public IEnumerable<SearchTermAssessment> Flagged => Assessments.Where(a => a.IsFlagged);

    public IEnumerable<SearchTermAssessment> NegativeKeywordCandidates
        => Assessments.Where(a => a.IsNegativeKeywordCandidate);

    /// <summary>Proposed negative keywords, most expensive first, for copying or export.</summary>
    public IReadOnlyList<string> SuggestedNegativeKeywords
        => [.. NegativeKeywordCandidates
            .OrderByDescending(a => a.Term.Metrics.Spend)
            .Select(a => a.Term.SearchTerm)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Total spend across all search terms returned by the report.</summary>
    public decimal SpendAnalysed => Assessments.Sum(a => a.Term.Metrics.Spend);

    /// <summary>Spend on terms that were flagged.</summary>
    public decimal FlaggedSpend => Flagged.Sum(a => a.Term.Metrics.Spend);

    /// <summary>Spend on flagged terms that recorded no conversions.</summary>
    public decimal UnproductiveSpend => Assessments.Sum(a => a.UnproductiveSpend);

    public int TermCount => Assessments.Count;

    public int FlaggedCount => Flagged.Count();
}
