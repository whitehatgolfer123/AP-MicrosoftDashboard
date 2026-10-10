using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Settings;

namespace MsAds.Auditor.Core.Analysis;

/// <summary>
/// Classifies search terms against the configured waste rules. Deterministic throughout: every
/// flag is a comparison against a configurable threshold or against the account's own averages.
/// </summary>
/// <remarks>
/// The analyser never proposes an automatic change. Its strongest output is a list of candidate
/// negative keywords for a human to review and apply manually.
/// </remarks>
public sealed class SearchTermWasteAnalyzer
{
    /// <summary>
    /// Words ignored when measuring how much a query overlaps the keyword it matched, so that
    /// shared filler words do not disguise a genuine mismatch.
    /// </summary>
    private static readonly HashSet<string> IgnoredWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "best", "but", "by", "can", "do", "does", "for", "from",
        "how", "i", "in", "is", "it", "me", "my", "near", "of", "on", "or", "the", "to", "top", "uk", "us",
        "was", "what", "when", "where", "which", "who", "why", "will", "with", "you", "your",
    };

    /// <summary>
    /// A term's conversion rate must fall below this fraction of the account's own conversion
    /// rate before it is called poor, so that ordinary variation is not flagged.
    /// </summary>
    private const decimal PoorConversionRateFraction = 0.5m;

    /// <summary>A term's CPC must exceed this multiple of the account average to count as expensive.</summary>
    private const decimal ExpensiveCpcMultiple = 1.75m;

    /// <summary>Below this share of meaningful words in common, matching is treated as loose.</summary>
    private const decimal LowOverlapThreshold = 0.34m;

    public SearchTermAnalysis Analyse(
        DataSet<SearchTermPerformance> searchTerms,
        AuditSettings settings,
        MetricSet accountMetrics,
        MetricFormatter formatter,
        bool conversionDataAvailable)
    {
        if (searchTerms.IsUnavailable)
        {
            return new SearchTermAnalysis
            {
                DataUnavailable = true,
                Notices = searchTerms.Notices,
            };
        }

        var accountConversionRate = accountMetrics.ConversionRate;
        var accountCpc = accountMetrics.AverageCpc;

        var assessments = searchTerms.Rows
            .Select(term => Assess(term, settings, formatter, accountConversionRate, accountCpc, conversionDataAvailable))
            .OrderByDescending(a => a.UnproductiveSpend)
            .ThenByDescending(a => a.Term.Metrics.Spend)
            .ToList();

        var notices = new List<DataNotice>(searchTerms.Notices);

        if (notices.All(n => n.Code != DataNotices.SearchTermCoverageCode))
        {
            notices.Add(DataNotices.SearchTermCoverage());
        }

        if (!conversionDataAvailable)
        {
            notices.Add(DataNotices.ConversionDataUnavailable("Search terms"));
        }

        return new SearchTermAnalysis
        {
            Assessments = assessments,
            Notices = notices,
        };
    }

    private SearchTermAssessment Assess(
        SearchTermPerformance term,
        AuditSettings settings,
        MetricFormatter formatter,
        decimal? accountConversionRate,
        decimal? accountCpc,
        bool conversionDataAvailable)
    {
        var metrics = term.Metrics;
        var flags = new List<SearchTermFlag>();
        var caveats = new List<string>();
        var reasons = new List<string>();

        var hasConversionSignal = conversionDataAvailable && metrics.HasConversionData;
        var conversions = metrics.Conversions ?? 0m;
        var meetsSpendBar = metrics.Spend >= settings.SearchTermMinimumSpend;
        var meetsClickBar = metrics.Clicks >= settings.SearchTermMinimumClicks;

        // Rule 1: spend above threshold with zero conversions. The headline waste signal.
        if (hasConversionSignal && conversions == 0m && meetsSpendBar)
        {
            flags.Add(SearchTermFlag.SpendWithNoConversions);
            reasons.Add(
                $"spent {formatter.FormatCurrency(metrics.Spend)} across {metrics.Clicks} click(s) with no recorded conversions, "
                + $"against a minimum spend threshold of {formatter.FormatCurrency(settings.SearchTermMinimumSpend)}");
        }

        // Rule 2: CPA above the configured target, where one exists.
        if (hasConversionSignal && settings.HasTargetCpa && metrics.CostPerAcquisition is { } cpa
            && cpa > settings.CpaFlagThreshold!.Value)
        {
            flags.Add(SearchTermFlag.CpaAboveTarget);
            reasons.Add(
                $"CPA of {formatter.FormatCurrency(cpa)} exceeds the {formatter.FormatCurrency(settings.TargetCpa)} target by more than the "
                + $"{settings.CpaToleranceAboveTargetPercent:N0}% tolerance");
        }

        // Rule 3: enough clicks to judge, but converting far below the account's own rate.
        if (hasConversionSignal && meetsClickBar && conversions > 0m
            && accountConversionRate is > 0m && metrics.ConversionRate is { } rate
            && rate < accountConversionRate.Value * PoorConversionRateFraction)
        {
            flags.Add(SearchTermFlag.PoorConversionRate);
            reasons.Add(
                $"converts at {formatter.FormatPercent(rate)} against an account average of {formatter.FormatPercent(accountConversionRate)}, "
                + $"on {metrics.Clicks} click(s)");
        }

        // Rule 4: unusually expensive clicks relative to the rest of the account.
        if (meetsSpendBar && accountCpc is > 0m && metrics.AverageCpc is { } termCpc
            && termCpc > accountCpc.Value * ExpensiveCpcMultiple)
        {
            flags.Add(SearchTermFlag.ExpensiveClicks);
            reasons.Add(
                $"average CPC of {formatter.FormatCurrency(termCpc)} is more than {ExpensiveCpcMultiple:N2}× the account average of "
                + formatter.FormatCurrency(accountCpc));
        }

        // Rule 5: wording overlap with the matched keyword. A lexical check, not a semantic one.
        var overlap = KeywordOverlap(term.SearchTerm, term.Keyword);

        if (overlap is { } ratio && ratio < LowOverlapThreshold && metrics.Clicks > 0)
        {
            flags.Add(SearchTermFlag.LowKeywordOverlap);
            reasons.Add(
                $"shares little wording with the keyword “{term.Keyword}” that matched it"
                + (term.DeliveredMatchType is null ? "" : $" via {term.DeliveredMatchType.ToLowerInvariant()} match"));
            caveats.Add(
                "Overlap is measured on words alone. A query can be a close commercial match while using entirely "
                + "different wording, so treat this as a prompt to look rather than as evidence of irrelevance.");
        }

        // Insufficient evidence: spending, but not yet enough to judge.
        if (flags.Count == 0 && metrics.Spend > 0m && (!meetsSpendBar || !meetsClickBar))
        {
            flags.Add(SearchTermFlag.InsufficientEvidence);
            reasons.Add(
                $"has {formatter.FormatCurrency(metrics.Spend)} of spend across {metrics.Clicks} click(s), below the configured "
                + $"thresholds of {formatter.FormatCurrency(settings.SearchTermMinimumSpend)} and {settings.SearchTermMinimumClicks} "
                + "click(s) needed to draw a conclusion");
        }

        if (!conversionDataAvailable)
        {
            caveats.Add(
                "Conversion data was unavailable, so this term could only be assessed on cost and traffic. "
                + "No conclusion about its conversion performance is implied.");
        }

        var confidence = Grade(metrics, settings, hasConversionSignal);
        var action = ChooseAction(flags, metrics, settings, hasConversionSignal, overlap);

        // A negative keyword is only proposed on the strongest evidence: real spend, real clicks,
        // confirmed zero conversions, and no competing explanation.
        var isCandidate =
            action == SearchTermAction.ConsiderNegativeKeyword
            && hasConversionSignal
            && conversions == 0m
            && meetsSpendBar
            && meetsClickBar;

        if (isCandidate)
        {
            caveats.Add(
                "Adding a negative keyword prevents all future matching of this query, including variations. "
                + "Review the wording before applying it. Nothing is applied automatically: a change is only "
                + "made when someone selects it and confirms it.");
        }

        return new SearchTermAssessment
        {
            Term = term,
            Flags = flags,
            RecommendedAction = action,
            Confidence = confidence,
            Caveats = caveats,
            Reason = reasons.Count == 0
                ? "No rule was triggered for this term."
                : char.ToUpperInvariant(reasons[0][0]) + reasons[0][1..]
                  + (reasons.Count > 1 ? "; also " + string.Join("; ", reasons.Skip(1)) : "")
                  + ".",
            UnproductiveSpend = hasConversionSignal && conversions == 0m ? metrics.Spend : 0m,
            IsNegativeKeywordCandidate = isCandidate,
        };
    }

    /// <summary>
    /// Picks the single most useful next step. Ordered so that the explanation most likely to be
    /// correct wins: a tracking or landing page problem is a better first explanation than
    /// irrelevance when the traffic does convert sometimes.
    /// </summary>
    private static SearchTermAction ChooseAction(
        List<SearchTermFlag> flags,
        MetricSet metrics,
        AuditSettings settings,
        bool hasConversionSignal,
        decimal? overlap)
    {
        if (flags.Count == 0 || flags.Contains(SearchTermFlag.InsufficientEvidence))
        {
            return SearchTermAction.ContinueMonitoring;
        }

        if (!hasConversionSignal)
        {
            return SearchTermAction.InvestigateIntent;
        }

        var conversions = metrics.Conversions ?? 0m;
        var zeroConversions = conversions == 0m;
        var enoughClicks = metrics.Clicks >= settings.SearchTermMinimumClicks;

        // Converting, just not well enough: the page or the tracking is the likelier cause.
        if (!zeroConversions && flags.Contains(SearchTermFlag.PoorConversionRate))
        {
            return SearchTermAction.CheckLandingPageAndTracking;
        }

        // Loose matching with no return points at the keyword rather than the query.
        if (zeroConversions && flags.Contains(SearchTermFlag.LowKeywordOverlap) && overlap is not null)
        {
            return enoughClicks
                ? SearchTermAction.ConsiderNegativeKeyword
                : SearchTermAction.ReviewKeywordOrMatchType;
        }

        if (zeroConversions && enoughClicks && flags.Contains(SearchTermFlag.SpendWithNoConversions))
        {
            return SearchTermAction.ConsiderNegativeKeyword;
        }

        if (zeroConversions)
        {
            return SearchTermAction.InvestigateIntent;
        }

        if (flags.Contains(SearchTermFlag.CpaAboveTarget) || flags.Contains(SearchTermFlag.ExpensiveClicks))
        {
            return SearchTermAction.ReviewKeywordOrMatchType;
        }

        return SearchTermAction.ContinueMonitoring;
    }

    private static FindingConfidence Grade(MetricSet metrics, AuditSettings settings, bool hasConversionSignal)
    {
        if (!hasConversionSignal)
        {
            return FindingConfidence.Low;
        }

        if (metrics.Clicks >= settings.SearchTermMinimumClicks * 2
            && metrics.Spend >= settings.SearchTermMinimumSpend * 2m)
        {
            return FindingConfidence.High;
        }

        return metrics.Clicks >= settings.SearchTermMinimumClicks && metrics.Spend >= settings.SearchTermMinimumSpend
            ? FindingConfidence.Medium
            : FindingConfidence.Low;
    }

    /// <summary>
    /// The share of the query's meaningful words that also appear in the keyword. Null when there
    /// is no keyword to compare against, or when neither side has a meaningful word.
    /// </summary>
    internal static decimal? KeywordOverlap(string searchTerm, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        var queryWords = Words(searchTerm);
        var keywordWords = Words(keyword);

        if (queryWords.Count == 0 || keywordWords.Count == 0)
        {
            return null;
        }

        var shared = queryWords.Count(w => keywordWords.Contains(w));
        return (decimal)shared / queryWords.Count;
    }

    private static HashSet<string> Words(string value)
        => [.. value
            .Split([' ', '\t', '-', '_', '/', ',', '.', '?', '!', '"', '\'', '(', ')', '+', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.Trim().ToLowerInvariant())
            .Where(w => w.Length > 1 && !IgnoredWords.Contains(w))];
}
