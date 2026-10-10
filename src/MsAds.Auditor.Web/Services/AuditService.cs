using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Runs one audit end to end: checks the user may see the account, resolves the reporting dates
/// in that account's own time zone, builds the report, and stores it.
/// </summary>
public sealed class AuditService(
    IAdsDataSource dataSource,
    IAccountDirectory accountDirectory,
    AuditReportBuilder builder,
    ReportingPeriodResolver periodResolver,
    IReportStore store,
    IOptions<AuditorOptions> options,
    ILogger<AuditService> logger)
{
    private readonly AuditorOptions _options = options.Value;

    public DataSourceKind SourceKind => dataSource.Kind;

    public string SourceName => dataSource.DisplayName;

    public async Task<AuditOutcome> RunAsync(AuditRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AccountId <= 0)
        {
            return AuditOutcome.Failure(
                "No account selected",
                "Enter a Microsoft Advertising account ID, or choose one of the accounts listed above.",
                accessFailure: AccountAccessFailure.InvalidFormat);
        }

        if (!request.Selection.Any)
        {
            return AuditOutcome.Failure(
                "No analyses selected",
                "Select at least one analysis to run before generating a report.");
        }

        try
        {
            // Never request a report for an account before confirming the user may see it.
            var access = await accountDirectory
                .ValidateAccountAsync(request.AccountId, cancellationToken)
                .ConfigureAwait(false);

            if (!access.IsAccessible || access.Account is null)
            {
                logger.LogWarning(
                    "Access denied for account {AccountId}: {Failure}", request.AccountId, access.Failure);

                return AuditOutcome.Failure(
                    "Account not accessible",
                    access.Message ?? $"Account {request.AccountId} could not be opened.",
                    Remedy(access.Failure),
                    canRetry: access.Failure is AccountAccessFailure.ServiceError,
                    accessFailure: access.Failure);
            }

            var account = access.Account;
            var timeZone = AccountTimeZoneResolver.Resolve(account.TimeZone);

            var period = periodResolver.Resolve(
                request.Period,
                timeZone,
                excludeCurrentDay: request.ExcludeToday,
                includeComparison: request.CompareWithPreviousPeriod);

            var settings = request.Settings ?? _options.Defaults;

            var report = await builder
                .BuildAsync(dataSource, account, period, settings, request.Selection, cancellationToken)
                .ConfigureAwait(false);

            store.Add(report);

            logger.LogInformation(
                "Report {ReportId} generated for account {AccountId} in {Duration}ms with {Findings} finding(s)",
                report.ReportId, account.AccountId, report.Duration.TotalMilliseconds,
                report.Recommendations.Findings.Count);

            return AuditOutcome.Success(report);
        }
        catch (OperationCanceledException)
        {
            return AuditOutcome.Failure(
                "Report cancelled",
                "The report was cancelled before it finished.",
                "Generate the report again when you are ready.",
                canRetry: true);
        }
        catch (Exception ex)
        {
            // Log the technical detail; show the user something useful without leaking internals.
            logger.LogError(ex, "Report generation failed for account {AccountId}", request.AccountId);

            return AuditOutcome.Failure(
                "Report could not be generated",
                "An unexpected error occurred while retrieving or analysing the data. The technical "
                + "detail has been written to the application log.",
                "Try generating the report again. If it keeps failing, check the application log and "
                + "the Microsoft Advertising service status.",
                canRetry: true);
        }
    }

    private static string Remedy(AccountAccessFailure? failure) => failure switch
    {
        AccountAccessFailure.InvalidFormat =>
            "Account IDs are numeric. Check the value and try again.",
        AccountAccessFailure.NotFound =>
            "Check the account ID. Note that a manager (customer) ID is not an advertising account ID — "
            + "the two are different identifiers and cannot be used interchangeably.",
        AccountAccessFailure.NoPermission =>
            "Ask an administrator of the manager account to grant the signed-in user access to this account.",
        AccountAccessFailure.NotAuthenticated =>
            "Sign in again to refresh the connection to Microsoft Advertising.",
        AccountAccessFailure.ServiceError =>
            "This is usually temporary. Wait a moment and try again.",
        _ => "Check the account ID and the connection status, then try again.",
    };
}
