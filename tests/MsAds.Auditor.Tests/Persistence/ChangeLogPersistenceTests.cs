using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Data.Stores;
using MsAds.Auditor.Tests.Support;

namespace MsAds.Auditor.Tests.Persistence;

/// <summary>
/// The change log against a real SQLite database created by the real migrations.
/// </summary>
/// <remarks>
/// This is the record of what the tool altered in a client's account, so these tests care about
/// two things above all: that a recorded run survives being read back in full, and that the
/// "already applied" question is answered correctly, because getting it wrong means either
/// applying a change twice or hiding one that never landed.
/// </remarks>
public class ChangeLogPersistenceTests
{
    [Fact]
    public async Task A_recorded_run_survives_a_restart_in_full()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var change = Proposal();
        var run = Run(change, ChangeStatus.Applied, "Added as a campaign negative.");

        // Written by one instance and read by another, which is what a restart amounts to:
        // nothing is served from the first store's memory.
        await NewLog(database).RecordAsync(run);

        var readBack = await NewLog(database).RecentAsync();

        var only = Assert.Single(readBack);
        Assert.Equal(run.RunId, only.RunId);
        Assert.Equal(run.RanAt, only.RanAt);
        Assert.Equal("tom@addpeople.co.uk", only.RanBy);
        Assert.Equal(1234567L, only.AccountId);
        Assert.Equal("Northern Plumbing", only.AccountName);
        Assert.False(only.WasPreview);

        var outcome = Assert.Single(only.Outcomes);
        Assert.Equal(ChangeStatus.Applied, outcome.Status);
        Assert.Equal("Added as a campaign negative.", outcome.Message);

        // The proposal is stored alongside the outcome, because the report it came from is never
        // persisted — without this the trail could not say what was changed.
        Assert.Equal(change.Id, outcome.Change.Id);
        Assert.Equal(ChangeActionKind.AddCampaignNegativeKeyword, outcome.Change.Kind);
        Assert.Equal(ChangeRisk.Additive, outcome.Change.Risk);
        Assert.Equal("free boiler", outcome.Change.Value);
        Assert.Equal("Phrase", outcome.Change.MatchType);
        Assert.Equal(98765L, outcome.Change.TargetEntityId);
        Assert.Equal("Campaign: Generic – Broad discovery", outcome.Change.TargetDescription);
        Assert.Equal("SearchTermWaste", outcome.Change.Source);
        Assert.Equal(412.55m, outcome.Change.SpendAtStake);
        Assert.Equal(["Check for legitimate variants first."], outcome.Change.Caveats);
    }

    [Fact]
    public async Task Money_is_stored_without_being_rounded()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        // SQLite has no decimal type, and left to the provider's default a decimal becomes a
        // floating-point REAL. Spend at stake is money, so the stored column is TEXT and the
        // value has to come back exactly.
        var change = Proposal() with { SpendAtStake = 1234.5678m };

        await NewLog(database).RecordAsync(Run(change, ChangeStatus.Applied, "done"));

        var readBack = await NewLog(database).RecentAsync();

        Assert.Equal(1234.5678m, readBack[0].Outcomes[0].Change.SpendAtStake);
    }

    [Fact]
    public async Task Runs_come_back_newest_first()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var log = NewLog(database);

        await log.RecordAsync(Run(Proposal(), ChangeStatus.Applied, "first", TestData.FixedNow.AddHours(-2)));
        await log.RecordAsync(Run(Proposal(), ChangeStatus.Applied, "second", TestData.FixedNow));

        var runs = await log.RecentAsync();

        Assert.Equal(2, runs.Count);
        Assert.Equal("second", runs[0].Outcomes[0].Message);
    }

    [Fact]
    public async Task Outcomes_keep_the_order_they_were_applied_in()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        // Ordering matters when reading back a partially failed batch: which change failed, and
        // what had already been sent before it, is the question being asked.
        var outcomes = Enumerable
            .Range(0, 5)
            .Select(i => new ChangeOutcome(
                Proposal() with { Id = $"change-{i}" }, ChangeStatus.Applied, $"step {i}"))
            .ToList();

        await NewLog(database).RecordAsync(new ChangeRunResult
        {
            RunId = Guid.NewGuid(),
            RanAt = TestData.FixedNow,
            AccountId = 1234567L,
            AccountName = "Northern Plumbing",
            WasPreview = false,
            Outcomes = outcomes,
        });

        var readBack = await NewLog(database).RecentAsync();

        Assert.Equal(
            ["change-0", "change-1", "change-2", "change-3", "change-4"],
            readBack[0].Outcomes.Select(o => o.Change.Id));
    }

    [Fact]
    public async Task An_applied_change_is_reported_as_applied()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var change = Proposal();

        await NewLog(database).RecordAsync(Run(change, ChangeStatus.Applied, "done"));

        Assert.Contains(change.Id, await NewLog(database).AppliedChangeIdsAsync(change.AccountId));
    }

    [Fact]
    public async Task A_previewed_change_is_not_reported_as_applied()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var change = Proposal();

        await NewLog(database).RecordAsync(new ChangeRunResult
        {
            RunId = Guid.NewGuid(),
            RanAt = TestData.FixedNow,
            AccountId = change.AccountId,
            AccountName = "Northern Plumbing",
            WasPreview = true,
            Outcomes = [new ChangeOutcome(change, ChangeStatus.Previewed, "would add")],
        });

        // Reviewing a change must not remove it from the proposal list; nothing was sent.
        Assert.Empty(await NewLog(database).AppliedChangeIdsAsync(change.AccountId));
    }

    [Theory]
    [InlineData(ChangeStatus.Failed)]
    [InlineData(ChangeStatus.Skipped)]
    public async Task A_change_that_did_not_land_stays_available_to_retry(ChangeStatus status)
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var change = Proposal();

        await NewLog(database).RecordAsync(Run(change, status, "did not land"));

        Assert.Empty(await NewLog(database).AppliedChangeIdsAsync(change.AccountId));
    }

    [Fact]
    public async Task Applied_ids_are_scoped_to_their_own_account()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var change = Proposal();

        await NewLog(database).RecordAsync(Run(change, ChangeStatus.Applied, "done"));

        Assert.Empty(await NewLog(database).AppliedChangeIdsAsync(accountId: 999));
    }

    [Fact]
    public async Task A_partially_failed_batch_records_each_item_separately()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var applied = Proposal() with { Id = "applied" };
        var failed = Proposal() with { Id = "failed" };

        await NewLog(database).RecordAsync(new ChangeRunResult
        {
            RunId = Guid.NewGuid(),
            RanAt = TestData.FixedNow,
            AccountId = applied.AccountId,
            AccountName = "Northern Plumbing",
            WasPreview = false,
            Outcomes =
            [
                new ChangeOutcome(applied, ChangeStatus.Applied, "done"),
                new ChangeOutcome(failed, ChangeStatus.Failed, "rejected by the API"),
            ],
        });

        var log = NewLog(database);
        var run = (await log.RecentAsync())[0];

        // One item failing must not mark the batch as all-applied or all-failed.
        Assert.Equal(1, run.AppliedCount);
        Assert.Equal(1, run.FailedCount);

        var appliedIds = await log.AppliedChangeIdsAsync(applied.AccountId);
        Assert.Contains("applied", appliedIds);
        Assert.DoesNotContain("failed", appliedIds);
    }

    [Fact]
    public async Task The_log_keeps_everything_by_default()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var log = NewLog(database);

        for (var i = 0; i < 20; i++)
        {
            await log.RecordAsync(Run(
                Proposal(), ChangeStatus.Applied, $"run {i}", TestData.FixedNow.AddMinutes(i)));
        }

        // An audit trail that forgets is not one, so the default limit of zero means no trimming.
        Assert.Equal(20, (await log.RecentAsync(limit: 100)).Count);
    }

    [Fact]
    public async Task A_configured_limit_drops_the_oldest_runs()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var log = new EfChangeLog(
            database.Factory,
            SqliteDatabase.Options(changeLogLimit: 3),
            SqliteDatabase.Logger<EfChangeLog>());

        for (var i = 0; i < 10; i++)
        {
            await log.RecordAsync(Run(
                Proposal(), ChangeStatus.Applied, $"run {i}", TestData.FixedNow.AddMinutes(i)));
        }

        var runs = await log.RecentAsync(limit: 100);

        Assert.Equal(3, runs.Count);

        // The newest are the ones kept.
        Assert.Equal(["run 9", "run 8", "run 7"], runs.Select(r => r.Outcomes[0].Message));
    }

    [Fact]
    public async Task Trimming_a_run_removes_its_outcomes_too()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var log = new EfChangeLog(
            database.Factory,
            SqliteDatabase.Options(changeLogLimit: 1),
            SqliteDatabase.Logger<EfChangeLog>());

        var stale = Proposal() with { Id = "stale" };
        await log.RecordAsync(Run(stale, ChangeStatus.Applied, "old", TestData.FixedNow.AddHours(-1)));
        await log.RecordAsync(Run(Proposal() with { Id = "fresh" }, ChangeStatus.Applied, "new"));

        var appliedIds = await log.AppliedChangeIdsAsync(stale.AccountId);

        // The cascade has to reach the outcomes. Were the run deleted but its outcome rows left
        // behind, the trimmed change would still be reported as applied and would never be
        // proposed again.
        Assert.DoesNotContain("stale", appliedIds);
        Assert.Contains("fresh", appliedIds);
    }

    private static EfChangeLog NewLog(SqliteDatabase database)
        => new(database.Factory, SqliteDatabase.Options(), SqliteDatabase.Logger<EfChangeLog>());

    private static ProposedChange Proposal() => new()
    {
        Id = "campaign-98765-free-boiler-phrase",
        Kind = ChangeActionKind.AddCampaignNegativeKeyword,
        Risk = ChangeRisk.Additive,
        AccountId = 1234567L,
        CustomerId = 7654321L,
        TargetEntityId = 98765L,
        TargetDescription = "Campaign: Generic – Broad discovery",
        Value = "free boiler",
        MatchType = "Phrase",
        Summary = "Exclude “free boiler” from Generic – Broad discovery.",
        Rationale = "£412.55 spent across 38 clicks with no conversions.",
        Source = "SearchTermWaste",
        SpendAtStake = 412.55m,
        Caveats = ["Check for legitimate variants first."],
    };

    private static ChangeRunResult Run(
        ProposedChange change, ChangeStatus status, string message, DateTimeOffset? at = null) => new()
        {
            RunId = Guid.NewGuid(),
            RanAt = at ?? TestData.FixedNow,
            RanBy = "tom@addpeople.co.uk",
            AccountId = change.AccountId,
            AccountName = "Northern Plumbing",
            WasPreview = false,
            Outcomes = [new ChangeOutcome(change, status, message)],
        };
}
