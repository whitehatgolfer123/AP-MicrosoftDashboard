using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;

namespace MsAds.Auditor.Core.Analysis;

/// <summary>
/// The direction an account has moved over the scanned period.
/// </summary>
/// <remarks>
/// Deliberately coarse. A scan exists to say which accounts deserve a proper look, not to
/// replace the full audit, so it reports a direction and the figures behind it rather than a
/// score that would imply more precision than one week of data supports.
/// </remarks>
public enum HealthVerdict
{
    /// <summary>Not enough data to say anything.</summary>
    Unknown,

    /// <summary>Delivered previously, nothing now. The most urgent state.</summary>
    NotDelivering,

    /// <summary>No delivery in either period; the account is dormant rather than broken.</summary>
    Dormant,

    Declining,

    Stable,

    Improving,
}

/// <summary>One account's position in a portfolio scan.</summary>
public sealed record AccountHealth
{
    public required AdAccountInfo Account { get; init; }

    public required MetricSet Current { get; init; }

    public MetricSet? Previous { get; init; }

    public required HealthVerdict Verdict { get; init; }

    /// <summary>Why the verdict was reached, in terms of the account's own figures.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// False when the verdict rests on cost and traffic alone because the account returned no
    /// conversion data. Shown so a green direction is not over-read.
    /// </summary>
    public required bool BasedOnConversions { get; init; }

    /// <summary>The headline comparisons, in display order.</summary>
    public IReadOnlyList<MetricComparison> KeyMetrics { get; init; } = [];

    /// <summary>Daily spend across the current period, for the row's sparkline.</summary>
    public IReadOnlyList<DailyMetrics> Daily { get; init; } = [];

    public IReadOnlyList<DataNotice> Notices { get; init; } = [];

    /// <summary>
    /// Formatter for this account's own currency. A scan can span accounts reporting in
    /// different currencies, so each row formats itself rather than sharing one formatter.
    /// </summary>
    public MetricFormatter Formatter { get; init; } = new();

    /// <summary>True when this account could not be scanned at all.</summary>
    public bool Failed { get; init; }

    public string? FailureMessage { get; init; }

    /// <summary>Accounts needing attention sort to the top of the scan.</summary>
    public int SortWeight => Verdict switch
    {
        HealthVerdict.NotDelivering => 0,
        HealthVerdict.Declining => 1,
        HealthVerdict.Unknown => 2,
        HealthVerdict.Stable => 3,
        HealthVerdict.Improving => 4,
        HealthVerdict.Dormant => 5,
        _ => 6,
    };

    public static AccountHealth ForFailure(AdAccountInfo account, string message) => new()
    {
        Account = account,
        Current = MetricSet.Empty,
        Verdict = HealthVerdict.Unknown,
        Reason = message,
        BasedOnConversions = false,
        Failed = true,
        FailureMessage = message,
    };
}

/// <summary>The result of scanning a set of accounts.</summary>
public sealed record PortfolioScan
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public required Time.ResolvedPeriod Period { get; init; }

    public IReadOnlyList<AccountHealth> Accounts { get; init; } = [];

    public TimeSpan Duration { get; init; }

    public required MetricFormatter Formatter { get; init; }

    /// <summary>True when the figures are synthetic.</summary>
    public required bool IsDemo { get; init; }

    public int CountOf(HealthVerdict verdict) => Accounts.Count(a => a.Verdict == verdict);

    public int FailedCount => Accounts.Count(a => a.Failed);

    /// <summary>Combined spend across every account scanned, in the shared currency.</summary>
    public decimal TotalSpend => Accounts.Sum(a => a.Current.Spend);

    public decimal? PreviousTotalSpend => Accounts.Any(a => a.Previous is not null)
        ? Accounts.Sum(a => a.Previous?.Spend ?? 0m)
        : null;

    /// <summary>
    /// True when the accounts scanned do not all report in the same currency, in which case the
    /// combined totals are not meaningful and are suppressed.
    /// </summary>
    public bool HasMixedCurrencies => Accounts
        .Select(a => a.Account.CurrencyCode)
        .Where(c => c is not null)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count() > 1;

    /// <summary>
    /// True when the accounts scanned do not all share a reporting time zone. The dates are
    /// resolved once for the whole scan, so for those accounts the window is shifted slightly
    /// against their own calendar.
    /// </summary>
    public bool HasMixedTimeZones => Accounts
        .Select(a => a.Account.TimeZone)
        .Where(t => t is not null)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count() > 1;

    /// <summary>Accounts ordered so the ones needing attention come first.</summary>
    public IReadOnlyList<AccountHealth> InAttentionOrder =>
        [.. Accounts.OrderBy(a => a.SortWeight).ThenByDescending(a => a.Current.Spend)];
}
