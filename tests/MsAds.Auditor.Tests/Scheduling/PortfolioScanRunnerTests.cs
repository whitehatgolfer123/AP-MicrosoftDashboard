using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Demo;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Data.Stores;
using MsAds.Auditor.Tests.Persistence;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Tests.Scheduling;

/// <summary>
/// The shared scan runner, against the demo data source and a real SQLite scan history.
/// </summary>
/// <remarks>
/// The runner is the one path both the Scan page and the background schedule take, so what is
/// tested here is the part neither of them should have to repeat: that a run is recorded, that
/// the comparison is against the state <em>before</em> this run rather than including it, and
/// that a failure is still recorded rather than vanishing.
/// </remarks>
public class PortfolioScanRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_scan_is_recorded_and_can_be_read_back()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        var outcome = await NewRunner(database, history)
            .RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Manual);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.Scan);
        Assert.NotEmpty(outcome.Scan.Accounts);

        var recorded = Assert.Single(await history.RecentAsync());
        Assert.Equal(outcome.RunId, recorded.RunId);
        Assert.Equal(ScanTrigger.Manual, recorded.Trigger);
        Assert.Equal(outcome.Scan.Accounts.Count, recorded.Accounts.Count);
    }

    [Fact]
    public async Task The_first_scan_reports_no_changes_because_there_is_nothing_to_compare()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var outcome = await NewRunner(database, NewHistory(database))
            .RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        // Otherwise the very first scheduled run would alert on every account at once.
        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
    }

    [Fact]
    public async Task A_verdict_that_moves_between_runs_is_reported_as_a_change()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        var accounts = await new DemoAccountDirectory().ListAccessibleAccountsAsync();
        var first = accounts[0];

        // A previous scan in which this account was Improving. Whatever the demo data produces
        // this time, it is not that, so the comparison has to notice.
        await history.RecordAsync(new ScanHistoryEntry
        {
            RunId = Guid.NewGuid(),
            GeneratedAt = Now.AddDays(-7),
            Trigger = ScanTrigger.Scheduled,
            PeriodStart = new DateOnly(2026, 10, 19),
            PeriodEnd = new DateOnly(2026, 10, 25),
            IsDemo = true,
            Accounts =
            [
                new ScanVerdict
                {
                    AccountId = first.AccountId,
                    CustomerId = first.CustomerId,
                    AccountName = first.AccountName,
                    Verdict = HealthVerdict.Improving,
                    BasedOnConversions = true,
                },
            ],
        });

        var outcome = await NewRunner(database, history)
            .RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        Assert.True(outcome.Succeeded);

        var nowVerdict = outcome.Scan!.Accounts
            .First(a => a.Account.AccountId == first.AccountId)
            .Verdict;

        if (nowVerdict == HealthVerdict.Improving)
        {
            // The demo data is deterministic, but not pinned by this test; if it happens to
            // agree there is correctly nothing to report.
            Assert.DoesNotContain(outcome.Changes, c => c.AccountId == first.AccountId);
            return;
        }

        var change = Assert.Single(outcome.Changes, c => c.AccountId == first.AccountId);
        Assert.Equal(HealthVerdict.Improving, change.From);
        Assert.Equal(nowVerdict, change.To);
    }

    [Fact]
    public async Task The_comparison_excludes_the_run_being_recorded()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);
        var runner = NewRunner(database, history);

        // Two identical scans back to back. If the comparison read the history after recording,
        // it would find this run's own verdicts and could never report a change — or, worse,
        // compare an account against itself and report every account as changed.
        await runner.RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);
        var second = await runner.RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        Assert.True(second.Succeeded);

        // The demo source is deterministic, so the same window gives the same verdicts and
        // nothing should have moved.
        Assert.Empty(second.Changes);
        Assert.Equal(2, (await history.RecentAsync()).Count);
    }

    [Fact]
    public async Task A_data_source_that_fails_everywhere_still_produces_a_recorded_run()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        var outcome = await NewRunner(database, history, dataSource: new ThrowingDataSource())
            .RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        // PortfolioScanner catches each account's failure separately, so one bad account cannot
        // lose the sweep. The scan therefore completes — with every account marked failed.
        Assert.True(outcome.Succeeded);
        Assert.All(outcome.Scan!.Accounts, a => Assert.True(a.Failed));

        var recorded = Assert.Single(await history.RecentAsync());
        Assert.Equal(ScanTrigger.Scheduled, recorded.Trigger);
        Assert.All(recorded.Accounts, a => Assert.True(a.Failed));
    }

    [Fact]
    public async Task A_scan_where_every_account_failed_is_worth_retrying()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var outcome = await NewRunner(database, NewHistory(database), dataSource: new ThrowingDataSource())
            .RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        // An expired token or a throttled API fails every account at once. Reading that as a
        // success would have the schedule record a run of Unknown verdicts and never retry.
        Assert.True(outcome.EveryAccountFailed);
        Assert.True(outcome.WorthRetrying);
        Assert.Contains("failed to scan", outcome.RetryReason);
    }

    [Fact]
    public async Task A_scan_in_which_some_accounts_succeeded_is_not_retried()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var outcome = await NewRunner(database, NewHistory(database))
            .RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        Assert.False(outcome.EveryAccountFailed);
        Assert.False(outcome.WorthRetrying);
    }

    [Fact]
    public async Task A_runner_level_failure_is_recorded_without_leaking_the_exception_message()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        // A history that cannot be read but can be written: a genuine runner-level failure,
        // after the scan itself has completed.
        var history = new UnreadableScanHistory(NewHistory(database));

        var outcome = await NewRunner(database, history)
            .RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        Assert.False(outcome.Succeeded);

        var recorded = Assert.Single(await history.Inner.RecentAsync());
        Assert.True(recorded.Failed);
        Assert.Empty(recorded.Accounts);

        // The row is shown in the interface, and an exception message can carry an account name
        // or a URL with a token in it. Only the type is kept.
        Assert.DoesNotContain(UnreadableScanHistory.SecretishMessage, recorded.FailureMessage);
        Assert.Contains(nameof(InvalidOperationException), recorded.FailureMessage);
    }

    [Fact]
    public async Task A_failure_listing_accounts_does_not_record_a_run()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        var outcome = await NewRunner(database, history, directory: new ThrowingAccountDirectory())
            .RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        Assert.False(outcome.Succeeded);
        Assert.Equal("The list of accessible accounts could not be retrieved.", outcome.FailureMessage);

        // Nothing was scanned and no period was ever resolved, so there is no run to record.
        Assert.Empty(await history.RecentAsync());
    }

    [Fact]
    public async Task Scanning_no_accounts_fails_rather_than_recording_an_empty_run()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        var outcome = await NewRunner(database, history)
            .RunAsync([], ReportingPeriod.Last7Days, ScanTrigger.Manual);

        Assert.False(outcome.Succeeded);
        Assert.Empty(await history.RecentAsync());
    }

    [Fact]
    public async Task The_trigger_distinguishes_a_scheduled_run_from_a_manual_one()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);
        var runner = NewRunner(database, history);

        await runner.RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Manual);
        await runner.RunForAllAccountsAsync(ReportingPeriod.Last7Days, ScanTrigger.Scheduled);

        var runs = await history.RecentAsync();

        Assert.Equal(2, runs.Count);
        Assert.Contains(runs, r => r.Trigger == ScanTrigger.Manual);
        Assert.Contains(runs, r => r.Trigger == ScanTrigger.Scheduled);
    }

    private static EfScanHistory NewHistory(SqliteDatabase database)
        => new(database.Factory, SqliteDatabase.Options(), SqliteDatabase.Logger<EfScanHistory>());

    private static PortfolioScanRunner NewRunner(
        SqliteDatabase database,
        IScanHistory history,
        IAdsDataSource? dataSource = null,
        IAccountDirectory? directory = null)
    {
        var clock = new FakeTimeProvider(Now);
        var source = dataSource ?? new DemoDataSource(new TestClock(Now));

        return new PortfolioScanRunner(
            directory ?? new DemoAccountDirectory(),
            source,
            new PortfolioScanner(source, new TestClock(Now), NullLogger<PortfolioScanner>.Instance),
            new ReportingPeriodResolver(new TestClock(Now)),
            new InMemorySettingsStore(
                new OptionsWrapper<AuditorOptions>(new AuditorOptions())),
            history,
            new OptionsWrapper<ScheduleOptions>(new ScheduleOptions()),
            clock,
            NullLogger<PortfolioScanRunner>.Instance);
    }

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>A data source that cannot retrieve anything, to exercise the failure path.</summary>
    private sealed class ThrowingDataSource : IAdsDataSource
    {
        private const string SecretishMessage =
            "Northern Plumbing (1234567) rejected token abc123";

        public DataSourceKind Kind => DataSourceKind.Live;

        public string DisplayName => "Throwing source";

        public Task<DataSet<DailyMetrics>> GetDailyMetricsAsync(
            ReportScope scope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretishMessage);

        public Task<DataSet<CampaignPerformance>> GetCampaignPerformanceAsync(
            ReportScope scope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretishMessage);

        public Task<DataSet<SearchTermPerformance>> GetSearchTermsAsync(
            ReportScope scope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretishMessage);

        public Task<DataSet<KeywordPerformance>> GetKeywordPerformanceAsync(
            ReportScope scope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretishMessage);

        public Task<DataSet<ChangeRecord>> GetChangeHistoryAsync(
            ReportScope scope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretishMessage);

        public Task<DataSet<NegativeKeywordConflict>> GetNegativeKeywordConflictsAsync(
            ReportScope scope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretishMessage);
    }

    /// <summary>
    /// Records fine but cannot be read, so the runner fails after the scan has completed.
    /// </summary>
    private sealed class UnreadableScanHistory(IScanHistory inner) : IScanHistory
    {
        internal const string SecretishMessage =
            "Northern Plumbing (1234567) rejected token abc123";

        internal IScanHistory Inner { get; } = inner;

        public Task RecordAsync(ScanHistoryEntry entry, CancellationToken cancellationToken = default)
            => Inner.RecordAsync(entry, cancellationToken);

        public Task<IReadOnlyList<ScanHistoryEntry>> RecentAsync(
            int limit = 50, CancellationToken cancellationToken = default)
            => Inner.RecentAsync(limit, cancellationToken);

        public Task<ScanHistoryEntry?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
            => Inner.GetAsync(runId, cancellationToken);

        public Task<IReadOnlyDictionary<long, HealthVerdict>> VerdictsBeforeAsync(
            DateTimeOffset before, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(SecretishMessage);
    }

    private sealed class ThrowingAccountDirectory : IAccountDirectory
    {
        public DataSourceKind Kind => DataSourceKind.Live;

        public Task<IReadOnlyList<AdAccountInfo>> ListAccessibleAccountsAsync(
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The directory is unavailable.");

        public Task<AccountAccessResult> ValidateAccountAsync(
            long accountId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The directory is unavailable.");
    }
}
