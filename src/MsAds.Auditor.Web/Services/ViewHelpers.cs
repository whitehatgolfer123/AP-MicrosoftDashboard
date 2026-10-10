using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Rules;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Maps domain values onto the presentation vocabulary. Kept in one place so that a severity or
/// a verdict always looks the same wherever it appears.
/// </summary>
public static class ViewHelpers
{
    /// <summary>
    /// The chip styling for a metric movement. Driven by the verdict, which already accounts for
    /// the metric's polarity, so a rise in spend is never styled as good news.
    /// </summary>
    public static string ChipClass(Verdict verdict) => verdict switch
    {
        Verdict.Positive => "chip chip--positive",
        Verdict.Negative => "chip chip--negative",
        Verdict.Neutral => "chip chip--neutral",
        _ => "chip chip--unknown",
    };

    /// <summary>
    /// A glyph for the direction of travel, so the change is legible without colour. Points at
    /// the movement in the value, not at whether the movement is welcome.
    /// </summary>
    public static string ChangeIcon(ChangeKind kind) => kind switch
    {
        ChangeKind.Increase or ChangeKind.NewActivity => "▲",
        ChangeKind.Decrease or ChangeKind.CeasedActivity => "▼",
        ChangeKind.NoChange => "■",
        _ => "–",
    };

    /// <summary>A word for the verdict, so colour is never the only carrier of meaning.</summary>
    public static string VerdictWord(Verdict verdict, ChangeKind kind)
    {
        if (kind is ChangeKind.NotComparable)
        {
            return "no comparison";
        }

        return verdict switch
        {
            Verdict.Positive => "favourable",
            Verdict.Negative => "adverse",
            _ => "neutral",
        };
    }

    public static string SeverityClass(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => "critical",
        FindingSeverity.Warning => "warning",
        FindingSeverity.Positive => "positive",
        _ => "information",
    };

    public static string SeverityLabel(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => "Needs attention",
        FindingSeverity.Warning => "Investigate",
        FindingSeverity.Positive => "Performing well",
        _ => "Information",
    };

    public static string SeverityIcon(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => "⛔",
        FindingSeverity.Warning => "⚠",
        FindingSeverity.Positive => "✓",
        _ => "ℹ",
    };

    public static string ConfidenceLabel(FindingConfidence confidence) => confidence switch
    {
        FindingConfidence.High => "High confidence",
        FindingConfidence.Medium => "Medium confidence",
        _ => "Low confidence — limited data",
    };

    public static string NoticeClass(NoticeSeverity severity) => severity switch
    {
        NoticeSeverity.Error => "notice notice--error",
        NoticeSeverity.Warning => "notice notice--warning",
        _ => "notice notice--info",
    };

    public static string NoticeIcon(NoticeSeverity severity) => severity switch
    {
        NoticeSeverity.Error => "⛔",
        NoticeSeverity.Warning => "⚠",
        _ => "ℹ",
    };

    public static string NoticeTitle(NoticeSeverity severity) => severity switch
    {
        NoticeSeverity.Error => "Data unavailable",
        NoticeSeverity.Warning => "Data limitation",
        _ => "Note",
    };

    /// <summary>Badge styling for a search term's recommended action.</summary>
    public static string ActionBadgeClass(SearchTermAction action) => action switch
    {
        SearchTermAction.ConsiderNegativeKeyword => "badge badge--critical",
        SearchTermAction.ReviewKeywordOrMatchType => "badge badge--warning",
        SearchTermAction.CheckLandingPageAndTracking => "badge badge--warning",
        SearchTermAction.InvestigateIntent => "badge badge--information",
        _ => "badge badge--muted",
    };

    /// <summary>Styling for an account's direction of travel on the portfolio scan.</summary>
    public static string VerdictClass(HealthVerdict verdict) => verdict switch
    {
        HealthVerdict.Improving => "positive",
        HealthVerdict.Declining => "critical",
        HealthVerdict.NotDelivering => "critical",
        HealthVerdict.Stable => "information",
        _ => "muted",
    };

    /// <summary>
    /// A glyph for the direction. Paired with a word everywhere it is used, so the reading never
    /// depends on colour or shape alone.
    /// </summary>
    public static string VerdictIcon(HealthVerdict verdict) => verdict switch
    {
        HealthVerdict.Improving => "▲",
        HealthVerdict.Declining => "▼",
        HealthVerdict.NotDelivering => "⛔",
        HealthVerdict.Stable => "■",
        HealthVerdict.Dormant => "–",
        _ => "?",
    };

    public static string VerdictLabel(HealthVerdict verdict) => verdict switch
    {
        HealthVerdict.Improving => "Improving",
        HealthVerdict.Declining => "Declining",
        HealthVerdict.NotDelivering => "Stopped delivering",
        HealthVerdict.Stable => "Stable",
        HealthVerdict.Dormant => "Dormant",
        _ => "Unknown",
    };

    /// <summary>
    /// A raw value for the client-side sorter. Numbers sort numerically; a missing value sorts
    /// last rather than as zero.
    /// </summary>
    public static string SortValue(decimal? value)
        => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public static string SortValue(long value)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
