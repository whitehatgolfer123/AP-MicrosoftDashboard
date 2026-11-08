using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Web.Services;

/// <summary>The outcome of one portfolio scan, whether a person or the schedule asked for it.</summary>
public sealed record ScanOutcome
{
    public required Guid RunId { get; init; }

    public PortfolioScan? Scan { get; init; }

    /// <summary>Accounts whose verdict moved since the previous scan, deteriorations first.</summary>
    public IReadOnlyList<VerdictChange> Changes { get; init; } = [];

    public string? FailureMessage { get; init; }

    /// <summary>True when the scan ran and produced verdicts.</summary>
    public bool Succeeded => Scan is not null && FailureMessage is null;

    /// <summary>
    /// True when the scan completed but every account in it failed.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Succeeded"/>, and it matters. PortfolioScanner catches each
    /// account's failure individually so that one bad account cannot lose the rest of the sweep,
    /// which means a systemic problem — an expired token, throttling, the network down —
    /// produces a "successful" scan in which nothing was actually read. Treating that as a
    /// success would have the schedule record a run of Unknown verdicts and not retry.
    /// </remarks>
    public bool EveryAccountFailed =>
        Scan is not null && Scan.Accounts.Count > 0 && Scan.FailedCount == Scan.Accounts.Count;

    /// <summary>
    /// Whether the schedule should back off and try again: either the run failed outright, or it
    /// came back with nothing readable in it.
    /// </summary>
    public bool WorthRetrying => !Succeeded || EveryAccountFailed;

    /// <summary>Why a retry is warranted, for the log.</summary>
    public string RetryReason => FailureMessage
        ?? (EveryAccountFailed
            ? $"all {Scan!.Accounts.Count} account(s) failed to scan"
            : "no reason");

    /// <summary>
    /// Accounts that moved to a verdict needing attention sooner than the one they held. This is
    /// what a notification is for.
    /// </summary>
    public IReadOnlyList<VerdictChange> Deteriorations =>
        [.. Changes.Where(c => c.IsDeterioration)];
}

/// <summary>
/// Runs a portfolio scan and records it.
/// </summary>
/// <remarks>
/// Shared by the Scan page and the background schedule, so an unattended scan goes through
/// exactly the same path as one a person asked for — including the account-level concurrency
/// limit, the verdict comparison and the history record. A separate scheduled code path would be
/// the obvious place for the two to drift apart.
/// </remarks>
public sealed class PortfolioScanRunner(
    IAccountDirectory accountDirectory,
    IAdsDataSource dataSource,
    PortfolioScanner scanner,
    ReportingPeriodResolver periodResolver,
    ISettingsStore settingsStore,
    IScanHistory history,
    IOptions<ScheduleOptions> scheduleOptions,
    TimeProvider timeProvider,
    ILogger<PortfolioScanRunner> logger)
{
    public async Task<ScanOutcome> RunAsync(
        IReadOnlyList<AdAccountInfo> accounts,
        ReportingPeriod period,
        ScanTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var runId = Guid.NewGuid();

        if (accounts.Count == 0)
        {
            return new ScanOutcome
            {
                RunId = runId,
                FailureMessage = "There were no accounts to scan.",
            };
        }

        // Dates are resolved once, from the first account's time zone, so every account in the
        // scan covers the same calendar days and the comparison is like for like.
        var timeZone = AccountTimeZoneResolver.Resolve(accounts[0].TimeZone);
        var resolved = periodResolver.Resolve(period, timeZone);

        try
        {
            var scan = await scanner
                .ScanAsync(
                    accounts,
                    resolved,
                    settingsStore.Current,
                    scheduleOptions.Value.MaxConcurrency,
                    cancellationToken)
                .ConfigureAwait(false);

            // Read before recording this run, or the comparison would find this scan's own
            // verdicts and every account would look unchanged.
            var previous = await history
                .VerdictsBeforeAsync(scan.GeneratedAt, cancellationToken)
                .ConfigureAwait(false);

            var entry = ScanHistoryEntry.From(scan, trigger, runId);

            await history.RecordAsync(entry, cancellationToken).ConfigureAwait(false);

            var changes = VerdictComparison.Between(previous, entry.Accounts);

            return new ScanOutcome { RunId = runId, Scan = scan, Changes = changes };
        }
        catch (OperationCanceledException)
        {
            // Shutdown, or the user navigating away. Not a failure worth recording.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "Portfolio scan {RunId} failed ({Trigger})", runId, trigger);

            // Recorded even though it failed: a scheduled run that produced nothing has to be
            // visible as having been attempted, or a silent failure looks the same as a schedule
            // that never fired.
            await RecordFailureAsync(runId, trigger, resolved.Current, exception, cancellationToken)
                .ConfigureAwait(false);

            return new ScanOutcome
            {
                RunId = runId,
                FailureMessage =
                    "The scan could not be completed. The detail has been written to the application log.",
            };
        }
    }

    /// <summary>
    /// Scans every account the connected user can reach. Used by the schedule, which has no
    /// person to choose a subset.
    /// </summary>
    public async Task<ScanOutcome> RunForAllAccountsAsync(
        ReportingPeriod period, ScanTrigger trigger, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();

        IReadOnlyList<AdAccountInfo> accounts;

        try
        {
            accounts = await accountDirectory
                .ListAccessibleAccountsAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not list accounts for scheduled scan {RunId}", runId);

            return new ScanOutcome
            {
                RunId = runId,
                FailureMessage = "The list of accessible accounts could not be retrieved.",
            };
        }

        return await RunAsync(accounts, period, trigger, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordFailureAsync(
        Guid runId,
        ScanTrigger trigger,
        DateRange period,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await history
                .RecordAsync(
                    ScanHistoryEntry.ForFailure(
                        runId,
                        timeProvider.GetUtcNow(),
                        trigger,
                        period,
                        dataSource.Kind == DataSourceKind.Demo,

                        // The exception type, not its message: a message can carry an account
                        // name or a URL with a token in it, and this row is read back in the
                        // interface.
                        $"The scan failed with {exception.GetType().Name}. "
                        + "The detail is in the application log."),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception recordFailure)
        {
            // Losing the failure record must not replace the original failure with a different
            // one, so this is logged and swallowed.
            logger.LogError(
                recordFailure, "Could not record the failure of scan {RunId}", runId);
        }
    }
}
