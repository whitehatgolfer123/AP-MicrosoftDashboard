using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Web.Services;

/// <summary>What the user asked for on the reporting controls.</summary>
public sealed record AuditRequest
{
    public long AccountId { get; init; }

    public ReportingPeriod Period { get; init; } = ReportingPeriod.Last30Days;

    /// <summary>Compare against the preceding equivalent period. On by default, per the brief.</summary>
    public bool CompareWithPreviousPeriod { get; init; } = true;

    /// <summary>Exclude the incomplete current day. On by default.</summary>
    public bool ExcludeToday { get; init; } = true;

    public AnalysisSelection Selection { get; init; } = new();

    /// <summary>Per-report threshold overrides; falls back to the configured defaults.</summary>
    public AuditSettings? Settings { get; init; }
}

/// <summary>
/// The result of attempting an audit: either a report, or an error stated in terms the user can
/// act on, with whether retrying is worthwhile.
/// </summary>
public sealed record AuditOutcome
{
    public AuditReport? Report { get; init; }

    public bool Succeeded => Report is not null;

    public string? ErrorTitle { get; init; }

    public string? ErrorDetail { get; init; }

    /// <summary>What the user should do about it.</summary>
    public string? ErrorRemedy { get; init; }

    public AccountAccessFailure? AccessFailure { get; init; }

    /// <summary>True for transient failures, where offering a retry makes sense.</summary>
    public bool CanRetry { get; init; }

    public static AuditOutcome Success(AuditReport report) => new() { Report = report };

    public static AuditOutcome Failure(
        string title, string detail, string? remedy = null, bool canRetry = false,
        AccountAccessFailure? accessFailure = null)
        => new()
        {
            ErrorTitle = title,
            ErrorDetail = detail,
            ErrorRemedy = remedy,
            CanRetry = canRetry,
            AccessFailure = accessFailure,
        };
}
