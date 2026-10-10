namespace MsAds.Auditor.Core.Models;

public enum NoticeSeverity
{
    /// <summary>Context the user should read, but nothing is wrong.</summary>
    Info,

    /// <summary>Data is usable but limited; conclusions need care.</summary>
    Warning,

    /// <summary>Data could not be retrieved; the related analysis is unavailable.</summary>
    Error,
}

/// <summary>
/// A statement about the completeness or trustworthiness of retrieved data. These are surfaced
/// in the interface rather than swallowed, because a missing search term report must never be
/// read as proof that no wasted spend exists.
/// </summary>
public sealed record DataNotice(
    string Code,
    string Message,
    NoticeSeverity Severity,
    string? Scope = null)
{
    public static DataNotice Info(string code, string message, string? scope = null)
        => new(code, message, NoticeSeverity.Info, scope);

    public static DataNotice Warning(string code, string message, string? scope = null)
        => new(code, message, NoticeSeverity.Warning, scope);

    public static DataNotice Error(string code, string message, string? scope = null)
        => new(code, message, NoticeSeverity.Error, scope);
}

/// <summary>Standard notices reused across data sources.</summary>
public static class DataNotices
{
    public const string SearchTermCoverageCode = "search-term-coverage";
    public const string DemoDataCode = "demo-data";
    public const string ConversionDataUnavailableCode = "conversion-data-unavailable";
    public const string ReportingLagCode = "reporting-lag";
    public const string EmptyPeriodCode = "empty-period";
    public const string PartialDayExcludedCode = "partial-day-excluded";

    /// <summary>
    /// Microsoft Advertising does not publish every query in the search term report. Shown
    /// wherever search term data is presented.
    /// </summary>
    public static DataNotice SearchTermCoverage() => DataNotice.Warning(
        SearchTermCoverageCode,
        "Microsoft Advertising does not report every search query. Low-volume queries and those "
        + "withheld for user-privacy reasons are excluded, so spend shown here is a subset of "
        + "total spend. Absence of a wasteful term in this table is not evidence that none exists.");

    public static DataNotice ReportingLag() => DataNotice.Info(
        ReportingLagCode,
        "Microsoft Advertising performance data can be restated for up to a few days after the "
        + "event, particularly conversions, which are credited to the click date and so can "
        + "continue to accrue against past days. Recent figures may still change.");

    public static DataNotice ConversionDataUnavailable(string? scope = null) => DataNotice.Warning(
        ConversionDataUnavailableCode,
        "Conversion data was not available for this report. Conversion-based metrics and any "
        + "recommendation that depends on them have been suppressed rather than treated as zero.",
        scope);

    public static DataNotice EmptyPeriod(string? scope = null) => DataNotice.Warning(
        EmptyPeriodCode,
        "No performance data was returned for the selected period. The account may have had no "
        + "delivery, or the reporting period may predate the account's activity.",
        scope);
}
