using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Core.Scheduling;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Runs the portfolio scan unattended, on the configured schedule.
/// </summary>
/// <remarks>
/// <para>
/// A plain hosted service rather than Hangfire or a cloud scheduler. ROADMAP.md names hosted
/// services as the starting point and Hangfire or Azure Functions "as scale requires"; Hangfire
/// needs a PostgreSQL or SQL Server store, which this deployment does not have, and one weekly
/// job on one machine does not need a job server.
/// </para>
/// <para>
/// Every scan is independently retryable: a failed attempt backs off and retries, and whatever
/// happens the next scheduled run is computed afresh, so a run that exhausts its retries is
/// simply left until next time rather than blocking the schedule.
/// </para>
/// </remarks>
public sealed class ScheduledScanService(
    IServiceScopeFactory scopeFactory,
    IOptions<ScheduleOptions> options,
    TimeProvider timeProvider,
    ILogger<ScheduledScanService> logger) : BackgroundService
{
    private readonly ScheduleOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "Scheduled scans are off. Set {Section}:Enabled to true to turn them on.",
                ScheduleOptions.SectionName);
            return;
        }

        foreach (var problem in _options.Problems())
        {
            logger.LogWarning("{Problem}", problem);
        }

        var days = _options.ResolvedDays;

        var timeZone = _options.ResolvedTimeZone;
        var timeOfDay = _options.ResolvedTimeOfDay;

        logger.LogInformation(
            "Scheduled scans are on: {Time} {TimeZone} on {Days}, covering {Period}.",
            timeOfDay, timeZone.Id, string.Join(", ", days), _options.Period);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var due = ScanSchedule.NextDue(now, timeOfDay, days, timeZone);

            if (due is null)
            {
                logger.LogError("No further scan is due; the schedule has stopped.");
                return;
            }

            logger.LogInformation("The next scheduled scan is due at {Due:u}.", due.Value);

            try
            {
                await Task
                    .Delay(ScanSchedule.DelayUntil(due.Value, now), timeProvider, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunWithRetriesAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunWithRetriesAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            // A fresh scope per attempt: the data source and the stores are resolved anew, so a
            // retry cannot reuse a connection or a cached account list left bad by the failure.
            using var scope = scopeFactory.CreateScope();

            var runner = scope.ServiceProvider.GetRequiredService<PortfolioScanRunner>();

            ScanOutcome outcome;

            try
            {
                outcome = await runner
                    .RunForAllAccountsAsync(_options.Period, ScanTrigger.Scheduled, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!outcome.WorthRetrying)
            {
                Report(outcome, scope.ServiceProvider);
                return;
            }

            if (attempt == _options.MaxRetries)
            {
                logger.LogError(
                    "The scheduled scan failed after {Attempts} attempt(s) and is left until the next "
                    + "scheduled run: {Reason}",
                    attempt + 1, outcome.RetryReason);

                // Still shown, if it produced anything: a scan of all-failed accounts is at
                // least evidence of which accounts could not be reached.
                if (outcome.Succeeded)
                {
                    Report(outcome, scope.ServiceProvider);
                }

                return;
            }

            var delay = ScanSchedule.RetryDelay(
                attempt + 1, _options.InitialRetryDelay, _options.MaximumRetryDelay);

            logger.LogWarning(
                "The scheduled scan failed ({Reason}); retrying in {Delay}. Attempt {Next} of {Max}.",
                outcome.RetryReason, delay, attempt + 2, _options.MaxRetries + 1);

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Says what the scan found, and makes the result available to the interface.
    /// </summary>
    /// <remarks>
    /// Only deteriorations are reported. ROADMAP.md Phase 7 is explicit that a notification
    /// should follow a job failing materially, an account needing attention, or a result
    /// changing state — not every completed run. A weekly "nothing changed" message trains
    /// people to ignore the channel it arrives on.
    ///
    /// Delivery is by log for now; Microsoft Graph mail is Phase 18.
    /// </remarks>
    private void Report(ScanOutcome outcome, IServiceProvider services)
    {
        var scan = outcome.Scan!;

        // Held so the Scan page shows the scheduled result rather than an empty screen.
        services.GetRequiredService<IScanStore>().Set(scan);

        if (outcome.Deteriorations.Count == 0)
        {
            logger.LogInformation(
                "Scheduled scan {RunId} finished: {Count} account(s), {Failed} could not be scanned, "
                + "nothing deteriorated.",
                outcome.RunId, scan.Accounts.Count, scan.FailedCount);
            return;
        }

        logger.LogWarning(
            "Scheduled scan {RunId} finished: {Count} account(s) need attention — {Accounts}",
            outcome.RunId,
            outcome.Deteriorations.Count,
            string.Join(
                "; ",
                outcome.Deteriorations.Select(d => $"{d.AccountName} {d.From} to {d.To}")));
    }
}
