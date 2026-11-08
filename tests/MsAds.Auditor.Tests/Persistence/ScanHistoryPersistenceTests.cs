using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Data.Stores;

namespace MsAds.Auditor.Tests.Persistence;

/// <summary>
/// The portfolio scan history, against a real SQLite database.
/// </summary>
/// <remarks>
/// Two concerns: that a verdict transition can be worked out across runs, which is what makes a
/// scheduled scan able to say what got worse, and that nothing beyond the verdict is stored.
/// </remarks>
public class ScanHistoryPersistenceTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = new(2026, 11, 3, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Wednesday = new(2026, 11, 4, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_recorded_scan_survives_a_restart()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var entry = Entry(Monday, ScanTrigger.Scheduled,
            Verdict(1, "Northern Plumbing", HealthVerdict.Declining),
            Verdict(2, "Harrogate Dental", HealthVerdict.Stable));

        await NewHistory(database).RecordAsync(entry);

        var readBack = Assert.Single(await NewHistory(database).RecentAsync());

        Assert.Equal(entry.RunId, readBack.RunId);
        Assert.Equal(Monday, readBack.GeneratedAt);
        Assert.Equal(ScanTrigger.Scheduled, readBack.Trigger);
        Assert.Equal(new DateOnly(2026, 10, 26), readBack.PeriodStart);
        Assert.Equal(new DateOnly(2026, 11, 1), readBack.PeriodEnd);
        Assert.Equal(TimeSpan.FromSeconds(12), readBack.Duration);
        Assert.True(readBack.IsDemo);
        Assert.False(readBack.Failed);

        Assert.Equal(2, readBack.Accounts.Count);
        Assert.Equal(1, readBack.CountOf(HealthVerdict.Declining));
        Assert.Equal(1, readBack.CountOf(HealthVerdict.Stable));
    }

    [Fact]
    public async Task A_scan_that_failed_outright_is_still_recorded()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        // A scheduled run that never produced verdicts has to be visible as having been
        // attempted, or a silent failure looks identical to a schedule that never fired.
        await NewHistory(database).RecordAsync(ScanHistoryEntry.ForFailure(
            Guid.NewGuid(),
            Monday,
            ScanTrigger.Scheduled,
            new Core.Time.DateRange(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1)),
            isDemo: false,
            "The account list could not be retrieved."));

        var readBack = Assert.Single(await NewHistory(database).RecentAsync());

        Assert.True(readBack.Failed);
        Assert.Equal("The account list could not be retrieved.", readBack.FailureMessage);
        Assert.Empty(readBack.Accounts);
    }

    [Fact]
    public async Task Scans_come_back_newest_first()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        await history.RecordAsync(Entry(Monday, ScanTrigger.Manual));
        await history.RecordAsync(Entry(Wednesday, ScanTrigger.Scheduled));
        await history.RecordAsync(Entry(Tuesday, ScanTrigger.Manual));

        var runs = await history.RecentAsync();

        Assert.Equal([Wednesday, Tuesday, Monday], runs.Select(r => r.GeneratedAt));
    }

    [Fact]
    public async Task The_previous_verdicts_are_the_ones_before_the_cutoff()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        await history.RecordAsync(Entry(Monday, ScanTrigger.Scheduled,
            Verdict(1, "Northern Plumbing", HealthVerdict.Stable)));

        await history.RecordAsync(Entry(Tuesday, ScanTrigger.Scheduled,
            Verdict(1, "Northern Plumbing", HealthVerdict.Declining)));

        var before = await history.VerdictsBeforeAsync(Tuesday);

        Assert.Equal(HealthVerdict.Stable, before[1]);
    }

    [Fact]
    public async Task An_accounts_last_known_verdict_is_used_even_if_it_missed_the_previous_run()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        // Stable on Monday, absent from Tuesday's run entirely — an account added to the scan
        // later, or one that errored. Comparing Wednesday against only Tuesday would treat it as
        // brand new and miss that it has deteriorated since Monday.
        await history.RecordAsync(Entry(Monday, ScanTrigger.Scheduled,
            Verdict(1, "Northern Plumbing", HealthVerdict.Stable),
            Verdict(2, "Harrogate Dental", HealthVerdict.Stable)));

        await history.RecordAsync(Entry(Tuesday, ScanTrigger.Scheduled,
            Verdict(2, "Harrogate Dental", HealthVerdict.Stable)));

        var before = await history.VerdictsBeforeAsync(Wednesday);

        Assert.Equal(HealthVerdict.Stable, before[1]);
        Assert.Equal(HealthVerdict.Stable, before[2]);
    }

    [Fact]
    public async Task A_failed_account_does_not_overwrite_its_last_real_verdict()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        await history.RecordAsync(Entry(Monday, ScanTrigger.Scheduled,
            Verdict(1, "Northern Plumbing", HealthVerdict.Improving)));

        // Tuesday could not reach the account. That is not evidence the account changed, so the
        // Monday verdict must remain the one to compare against.
        await history.RecordAsync(Entry(Tuesday, ScanTrigger.Scheduled,
            Verdict(1, "Northern Plumbing", HealthVerdict.Unknown) with { Failed = true }));

        var before = await history.VerdictsBeforeAsync(Wednesday);

        Assert.Equal(HealthVerdict.Improving, before[1]);
    }

    [Fact]
    public async Task A_scan_that_failed_outright_is_ignored_when_comparing()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = NewHistory(database);

        await history.RecordAsync(Entry(Monday, ScanTrigger.Scheduled,
            Verdict(1, "Northern Plumbing", HealthVerdict.Stable)));

        await history.RecordAsync(ScanHistoryEntry.ForFailure(
            Guid.NewGuid(), Tuesday, ScanTrigger.Scheduled,
            new Core.Time.DateRange(new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 2)),
            isDemo: true, "Throttled by the API."));

        var before = await history.VerdictsBeforeAsync(Wednesday);

        Assert.Equal(HealthVerdict.Stable, before[1]);
    }

    [Fact]
    public async Task The_history_is_trimmed_to_the_configured_limit()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var history = new EfScanHistory(
            database.Factory,
            SqliteDatabase.Options(scanHistoryLimit: 3),
            SqliteDatabase.Logger<EfScanHistory>());

        for (var i = 0; i < 8; i++)
        {
            await history.RecordAsync(Entry(Monday.AddDays(i), ScanTrigger.Scheduled));
        }

        var runs = await history.RecentAsync(limit: 100);

        Assert.Equal(3, runs.Count);
        Assert.Equal(Monday.AddDays(7), runs[0].GeneratedAt);
    }

    [Fact]
    public async Task A_run_can_be_fetched_by_its_id()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var entry = Entry(Monday, ScanTrigger.Manual, Verdict(1, "Northern Plumbing", HealthVerdict.Stable));

        await NewHistory(database).RecordAsync(entry);

        var found = await NewHistory(database).GetAsync(entry.RunId);
        Assert.NotNull(found);
        Assert.Equal(entry.RunId, found.RunId);

        Assert.Null(await NewHistory(database).GetAsync(Guid.NewGuid()));
    }

    // --- verdict comparison, which is pure and needs no database ---------------------------

    [Fact]
    public void A_move_towards_needing_attention_is_a_deterioration()
    {
        var changes = VerdictComparison.Between(
            new Dictionary<long, HealthVerdict> { [1] = HealthVerdict.Stable },
            [Verdict(1, "Northern Plumbing", HealthVerdict.NotDelivering)]);

        var change = Assert.Single(changes);
        Assert.True(change.IsDeterioration);
        Assert.Equal(HealthVerdict.Stable, change.From);
        Assert.Equal(HealthVerdict.NotDelivering, change.To);
    }

    [Fact]
    public void A_move_away_from_needing_attention_is_not_a_deterioration()
    {
        var changes = VerdictComparison.Between(
            new Dictionary<long, HealthVerdict> { [1] = HealthVerdict.Declining },
            [Verdict(1, "Northern Plumbing", HealthVerdict.Improving)]);

        Assert.False(Assert.Single(changes).IsDeterioration);
    }

    [Fact]
    public void Going_dormant_from_stable_counts_as_a_deterioration()
    {
        // An account that stopped spending altogether is worth a message, even though a dormant
        // account is the least interesting row in a scan listing.
        var changes = VerdictComparison.Between(
            new Dictionary<long, HealthVerdict> { [1] = HealthVerdict.Stable },
            [Verdict(1, "Northern Plumbing", HealthVerdict.Dormant)]);

        Assert.True(Assert.Single(changes).IsDeterioration);
    }

    [Fact]
    public void An_unchanged_verdict_is_not_reported()
    {
        var changes = VerdictComparison.Between(
            new Dictionary<long, HealthVerdict> { [1] = HealthVerdict.Stable },
            [Verdict(1, "Northern Plumbing", HealthVerdict.Stable)]);

        Assert.Empty(changes);
    }

    [Fact]
    public void An_account_with_no_previous_verdict_is_not_reported_as_a_change()
    {
        // Nothing to compare against, so calling it a change would mean every newly onboarded
        // account generated an alert on its first scan.
        var changes = VerdictComparison.Between(
            new Dictionary<long, HealthVerdict>(),
            [Verdict(1, "Northern Plumbing", HealthVerdict.NotDelivering)]);

        Assert.Empty(changes);
    }

    [Fact]
    public void Deteriorations_are_listed_before_improvements()
    {
        var changes = VerdictComparison.Between(
            new Dictionary<long, HealthVerdict>
            {
                [1] = HealthVerdict.Declining,
                [2] = HealthVerdict.Stable,
            },
            [
                Verdict(1, "Aardvark Autos", HealthVerdict.Improving),
                Verdict(2, "Zebra Zinc", HealthVerdict.NotDelivering),
            ]);

        // Alphabetically Aardvark would come first; the deterioration outranks it.
        Assert.Equal(["Zebra Zinc", "Aardvark Autos"], changes.Select(c => c.AccountName));
    }

    private static EfScanHistory NewHistory(SqliteDatabase database)
        => new(database.Factory, SqliteDatabase.Options(), SqliteDatabase.Logger<EfScanHistory>());

    private static ScanVerdict Verdict(long accountId, string name, HealthVerdict verdict) => new()
    {
        AccountId = accountId,
        CustomerId = 9000 + accountId,
        AccountName = name,
        Verdict = verdict,
        BasedOnConversions = true,
    };

    private static ScanHistoryEntry Entry(
        DateTimeOffset at, ScanTrigger trigger, params ScanVerdict[] accounts) => new()
        {
            RunId = Guid.NewGuid(),
            GeneratedAt = at,
            Trigger = trigger,
            PeriodStart = new DateOnly(2026, 10, 26),
            PeriodEnd = new DateOnly(2026, 11, 1),
            Duration = TimeSpan.FromSeconds(12),
            IsDemo = true,
            Accounts = accounts,
        };
}
