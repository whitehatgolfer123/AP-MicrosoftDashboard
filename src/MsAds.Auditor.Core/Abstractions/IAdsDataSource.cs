using MsAds.Auditor.Core.Models;

namespace MsAds.Auditor.Core.Abstractions;

public enum DataSourceKind
{
    /// <summary>Generated sample data, for development and demonstration. Never live performance.</summary>
    Demo,

    /// <summary>Live data retrieved from the Microsoft Advertising API.</summary>
    Live,
}

/// <summary>
/// Retrieves advertising data for a scope. Implemented once against generated demo data and once
/// against the Microsoft Advertising Reporting API, so every analysis above this seam is testable
/// without credentials.
/// </summary>
/// <remarks>
/// Implementations are read-only by contract: there is deliberately no method here that changes
/// anything in an advertising account.
/// </remarks>
public interface IAdsDataSource
{
    DataSourceKind Kind { get; }

    /// <summary>A short label for the interface, e.g. "Demo data" or "Microsoft Advertising API".</summary>
    string DisplayName { get; }

    /// <summary>Daily totals for the trend charts.</summary>
    Task<DataSet<DailyMetrics>> GetDailyMetricsAsync(ReportScope scope, CancellationToken cancellationToken = default);

    Task<DataSet<CampaignPerformance>> GetCampaignPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default);

    Task<DataSet<SearchTermPerformance>> GetSearchTermsAsync(ReportScope scope, CancellationToken cancellationToken = default);

    Task<DataSet<KeywordPerformance>> GetKeywordPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default);

    Task<DataSet<ChangeRecord>> GetChangeHistoryAsync(ReportScope scope, CancellationToken cancellationToken = default);

    Task<DataSet<NegativeKeywordConflict>> GetNegativeKeywordConflictsAsync(ReportScope scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lists and validates the accounts the authenticated user can reach, so a typed-in account id
/// can be checked before any report is requested.
/// </summary>
public interface IAccountDirectory
{
    DataSourceKind Kind { get; }

    Task<IReadOnlyList<AdAccountInfo>> ListAccessibleAccountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the authenticated user may audit the given advertising account, returning a
    /// message suitable for display when they may not.
    /// </summary>
    Task<AccountAccessResult> ValidateAccountAsync(long accountId, CancellationToken cancellationToken = default);
}
