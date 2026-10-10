namespace MsAds.Auditor.Core.Analysis;

/// <summary>Why a search term was flagged. A term can carry more than one flag.</summary>
public enum SearchTermFlag
{
    /// <summary>Spend above the configured threshold with zero recorded conversions.</summary>
    SpendWithNoConversions,

    /// <summary>Cost per acquisition above the configured target CPA.</summary>
    CpaAboveTarget,

    /// <summary>Enough clicks to judge, but a conversion rate well below the account's own.</summary>
    PoorConversionRate,

    /// <summary>Cost per click well above the account average, so each click is unusually expensive.</summary>
    ExpensiveClicks,

    /// <summary>
    /// The query shares little wording with the keyword that matched it, which can indicate loose
    /// matching. A wording heuristic, not a judgement of meaning.
    /// </summary>
    LowKeywordOverlap,

    /// <summary>Spend is accumulating but there is not yet enough data to judge.</summary>
    InsufficientEvidence,
}

/// <summary>The action proposed for a search term. The application never applies these itself.</summary>
public enum SearchTermAction
{
    /// <summary>Not enough evidence to act; keep watching.</summary>
    ContinueMonitoring,

    /// <summary>Work out what the searcher wanted before deciding.</summary>
    InvestigateIntent,

    /// <summary>Strong candidate for exclusion.</summary>
    ConsiderNegativeKeyword,

    /// <summary>The matching keyword or its match type is the likelier problem.</summary>
    ReviewKeywordOrMatchType,

    /// <summary>Traffic looks relevant but does not convert; check the page and the tracking.</summary>
    CheckLandingPageAndTracking,
}

public static class SearchTermDisplay
{
    public static string Label(this SearchTermAction action) => action switch
    {
        SearchTermAction.ContinueMonitoring => "Continue monitoring",
        SearchTermAction.InvestigateIntent => "Investigate search intent",
        SearchTermAction.ConsiderNegativeKeyword => "Consider adding as a negative keyword",
        SearchTermAction.ReviewKeywordOrMatchType => "Review the keyword or match type",
        SearchTermAction.CheckLandingPageAndTracking => "Check the landing page and conversion tracking",
        _ => action.ToString(),
    };

    public static string Label(this SearchTermFlag flag) => flag switch
    {
        SearchTermFlag.SpendWithNoConversions => "Spend with no conversions",
        SearchTermFlag.CpaAboveTarget => "CPA above target",
        SearchTermFlag.PoorConversionRate => "Poor conversion rate",
        SearchTermFlag.ExpensiveClicks => "Expensive clicks",
        SearchTermFlag.LowKeywordOverlap => "Little overlap with matched keyword",
        SearchTermFlag.InsufficientEvidence => "Insufficient evidence",
        _ => flag.ToString(),
    };
}
