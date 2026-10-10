using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.Abstractions;

/// <summary>
/// Identifies exactly what a data source should retrieve: which account, under which manager,
/// and over which dates.
/// </summary>
public sealed record ReportScope
{
    public required long AccountId { get; init; }

    /// <summary>The manager (customer) id that owns the account. Not interchangeable with the account id.</summary>
    public required long CustomerId { get; init; }

    public required DateRange Range { get; init; }

    /// <summary>The account's reporting currency, carried through for formatting.</summary>
    public string? CurrencyCode { get; init; }

    /// <summary>
    /// The account's reporting time zone exactly as the platform reported it. Passed back to the
    /// Reporting API so that the day boundaries it uses match the ones the dates were resolved
    /// against.
    /// </summary>
    public string? ReportingTimeZone { get; init; }

    public static ReportScope For(AdAccountInfo account, DateRange range) => new()
    {
        AccountId = account.AccountId,
        CustomerId = account.CustomerId,
        Range = range,
        CurrencyCode = account.CurrencyCode,
        ReportingTimeZone = account.TimeZone,
    };

    public ReportScope ForRange(DateRange range) => this with { Range = range };
}
