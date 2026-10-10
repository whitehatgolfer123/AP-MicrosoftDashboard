using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.Analysis;

/// <summary>
/// Scans many accounts for a quick direction-of-travel reading.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately lightweight: it requests only account-level daily totals for the two periods,
/// not the campaign, keyword, search term and change reports a full audit pulls. Scanning fifty
/// accounts therefore costs two small reports each rather than twelve large ones, which is what
/// makes it usable as a morning overview.
/// </para>
/// <para>
/// Accounts are scanned a few at a time rather than all at once, because Microsoft Advertising
/// throttles concurrent report requests and an agency scan could otherwise fire dozens
/// simultaneously. One account failing never fails the scan — it appears in the results with its
/// error, so a partial scan is still useful and the gap is visible.
/// </para>
/// </remarks>
public sealed class PortfolioScanner(IAdsDataSource dataSource, IClock clock, ILogger<PortfolioScanner> logger)
{
    /// <summary>How many accounts are queried at once. Kept low to stay well inside API limits.</summary>
    public const int DefaultConcurrency = 4;

    public async Task<PortfolioScan> ScanAsync(
        IReadOnlyList<AdAccountInfo> accounts,
        ResolvedPeriod period,
        AuditSettings settings,
        int maxConcurrency = DefaultConcurrency,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            "Scanning {Count} account(s) over {Range}", accounts.Count, period.Current);

        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        var results = await Task.WhenAll(accounts.Select(async account =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await ScanAccountAsync(account, period, settings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        stopwatch.Stop();

        // The scan's own formatter uses whichever currency the accounts agree on; where they
        // differ the combined totals are suppressed in the interface rather than added up.
        var sharedCurrency = accounts
            .Select(a => a.CurrencyCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation(
            "Scan of {Count} account(s) finished in {Seconds:N1}s with {Failed} failure(s)",
            accounts.Count, stopwatch.Elapsed.TotalSeconds, results.Count(r => r.Failed));

        return new PortfolioScan
        {
            GeneratedAt = clock.UtcNow,
            Period = period,
            Accounts = results,
            Duration = stopwatch.Elapsed,
            IsDemo = dataSource.Kind == DataSourceKind.Demo,
            Formatter = new MetricFormatter(
                settings.CultureName,
                sharedCurrency.Count == 1 ? sharedCurrency[0] : settings.CurrencyCode),
        };
    }

    private async Task<AccountHealth> ScanAccountAsync(
        AdAccountInfo account,
        ResolvedPeriod period,
        AuditSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var scope = ReportScope.For(account, period.Current);

            var currentTask = dataSource.GetDailyMetricsAsync(scope, cancellationToken);
            var previousTask = period.Previous is { } previousRange
                ? dataSource.GetDailyMetricsAsync(scope.ForRange(previousRange), cancellationToken)
                : Task.FromResult(new DataSet<DailyMetrics>());

            await Task.WhenAll(currentTask, previousTask).ConfigureAwait(false);

            var currentData = await currentTask.ConfigureAwait(false);
            var previousData = await previousTask.ConfigureAwait(false);

            if (currentData.IsUnavailable)
            {
                return AccountHealth.ForFailure(
                    account,
                    currentData.Notices.FirstOrDefault()?.Message
                    ?? "The account's performance report could not be retrieved.");
            }

            var current = MetricSet.Sum(currentData.Rows.Select(d => d.Metrics));
            var previous = period.Previous is null || previousData.IsEmpty
                ? null
                : MetricSet.Sum(previousData.Rows.Select(d => d.Metrics));

            // Each account is formatted in its own currency; a scan can span several.
            var formatter = new MetricFormatter(settings.CultureName, account.CurrencyCode ?? settings.CurrencyCode);
            var (verdict, reason, basedOnConversions) = HealthAssessor.Assess(current, previous, settings, formatter);

            return new AccountHealth
            {
                Account = account,
                Current = current,
                Previous = previous,
                Verdict = verdict,
                Reason = reason,
                BasedOnConversions = basedOnConversions,
                Formatter = formatter,
                KeyMetrics = HealthAssessor.KeyMetrics(current, previous),
                Daily = currentData.Rows,
                Notices = [.. currentData.Notices, .. previousData.Notices],
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A single account's failure must not lose the rest of the scan.
            logger.LogError(ex, "Scanning account {AccountId} failed", account.AccountId);

            return AccountHealth.ForFailure(
                account,
                "This account could not be scanned. The detail has been written to the application log.");
        }
    }
}
