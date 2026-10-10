using Microsoft.Extensions.Logging.Abstractions;
using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Core.Demo;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Tests.Support;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Tests.Changes;

/// <summary>
/// Which findings become applicable changes, and the gates that stand between a proposal and a
/// live account.
/// </summary>
public class ChangeProposalTests
{
    private static readonly FixedClock Clock = new(TestData.FixedNow);

    private static async Task<AuditReport> DemoReportAsync()
    {
        var period = new ReportingPeriodResolver(Clock).Resolve(ReportingPeriod.Last30Days, TimeZoneInfo.Utc);

        return await TestData.Builder(Clock).BuildAsync(
            new DemoDataSource(Clock),
            DemoAccounts.Primary,
            period,
            new AuditSettings { TargetCpa = 60m, MinimumSpend = 50m },
            AnalysisSelection.All);
    }

    [Fact]
    public async Task Wasteful_search_terms_become_negative_keyword_proposals()
    {
        var proposals = ChangeProposalBuilder.Build(await DemoReportAsync());

        var negatives = proposals.Where(p => p.Kind == ChangeActionKind.AddCampaignNegativeKeyword).ToList();

        Assert.NotEmpty(negatives);
        Assert.All(negatives, p =>
        {
            Assert.Equal(ChangeRisk.Additive, p.Risk);
            Assert.False(string.IsNullOrWhiteSpace(p.Value));
            Assert.NotEmpty(p.Caveats);
        });
    }

    [Fact]
    public async Task Negative_keywords_are_proposed_as_exact_match_only()
    {
        var proposals = ChangeProposalBuilder.Build(await DemoReportAsync());

        // Exact blocks the query actually observed wasting money and nothing wider; a phrase
        // negative is a judgement call left to the person.
        Assert.All(
            proposals.Where(p => p.Kind == ChangeActionKind.AddCampaignNegativeKeyword),
            p => Assert.Equal("Exact", p.MatchType));
    }

    [Fact]
    public async Task Every_proposal_states_its_target_rationale_and_source()
    {
        var proposals = ChangeProposalBuilder.Build(await DemoReportAsync());

        Assert.NotEmpty(proposals);
        Assert.All(proposals, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Summary));
            Assert.False(string.IsNullOrWhiteSpace(p.Rationale));
            Assert.False(string.IsNullOrWhiteSpace(p.TargetDescription));
            Assert.False(string.IsNullOrWhiteSpace(p.Source));
            Assert.True(p.TargetEntityId > 0);
        });
    }

    [Fact]
    public async Task Proposal_identifiers_are_stable_across_runs()
    {
        var first = ChangeProposalBuilder.Build(await DemoReportAsync()).Select(p => p.Id).ToList();
        var second = ChangeProposalBuilder.Build(await DemoReportAsync()).Select(p => p.Id).ToList();

        // A selection has to survive the review step, so the same proposal keeps the same id.
        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
    }

    [Fact]
    public async Task Additive_changes_are_listed_before_ones_that_stop_delivery()
    {
        var proposals = ChangeProposalBuilder.Build(await DemoReportAsync());
        var firstStop = proposals.ToList().FindIndex(p => p.Risk == ChangeRisk.StopsDelivery);
        var lastAdditive = proposals.ToList().FindLastIndex(p => p.Risk == ChangeRisk.Additive);

        if (firstStop >= 0 && lastAdditive >= 0)
        {
            Assert.True(lastAdditive < firstStop);
        }
    }

    [Fact]
    public void Nothing_is_permitted_while_changes_are_switched_off()
    {
        var options = new ChangeOptions { Enabled = false, AllowPausing = true };
        var change = Proposal(ChangeRisk.Additive);

        Assert.False(options.Permits(change));
        Assert.Contains("switched off", options.RefusalReason(change));
    }

    [Fact]
    public void Exclusions_can_be_allowed_without_allowing_pausing()
    {
        var options = new ChangeOptions { Enabled = true, AllowPausing = false };

        Assert.True(options.Permits(Proposal(ChangeRisk.Additive)));
        Assert.False(options.Permits(Proposal(ChangeRisk.StopsDelivery)));
        Assert.Contains("stop delivery", options.RefusalReason(Proposal(ChangeRisk.StopsDelivery)));
    }

    [Fact]
    public void Pausing_is_permitted_only_when_both_switches_are_on()
    {
        var options = new ChangeOptions { Enabled = true, AllowPausing = true };

        Assert.True(options.Permits(Proposal(ChangeRisk.StopsDelivery)));
        Assert.Null(options.RefusalReason(Proposal(ChangeRisk.StopsDelivery)));
    }

    [Fact]
    public async Task The_disabled_applier_refuses_rather_than_silently_doing_nothing()
    {
        var applier = new DisabledChangeApplier("Changes are off.");

        Assert.False(applier.CanApply);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => applier.ApplyAsync([Proposal(ChangeRisk.Additive)], "someone", preview: false));
    }

    [Fact]
    public async Task A_preview_sends_nothing_and_reports_the_call_it_would_make()
    {
        var applier = new DemoChangeApplier(Clock, NullLogger<DemoChangeApplier>.Instance);

        var result = await applier.ApplyAsync([Proposal(ChangeRisk.Additive)], "tom", preview: true);

        Assert.True(result.WasPreview);
        Assert.Equal(0, result.AppliedCount);
        Assert.All(result.Outcomes, o => Assert.Equal(ChangeStatus.Previewed, o.Status));
        Assert.Contains("Would call", result.Outcomes[0].Message);
        Assert.Contains("AddNegativeKeywordsToEntities", result.Outcomes[0].Message);
    }

    [Fact]
    public async Task A_demo_run_is_labelled_as_simulated()
    {
        var applier = new DemoChangeApplier(Clock, NullLogger<DemoChangeApplier>.Instance);

        var result = await applier.ApplyAsync([Proposal(ChangeRisk.Additive)], "tom", preview: false);

        Assert.True(applier.IsSimulated);
        Assert.Equal(1, result.AppliedCount);
        Assert.Contains("Simulated", result.Outcomes[0].Message);
    }

    [Fact]
    public async Task A_run_records_who_asked_for_it()
    {
        var applier = new DemoChangeApplier(Clock, NullLogger<DemoChangeApplier>.Instance);

        var result = await applier.ApplyAsync([Proposal(ChangeRisk.Additive)], "tom@addpeople.co.uk", preview: false);

        Assert.Equal("tom@addpeople.co.uk", result.RanBy);
    }

    [Fact]
    public void The_change_log_keeps_runs_newest_first()
    {
        var log = new InMemoryChangeLog();

        log.Record(Run(TestData.FixedNow.AddMinutes(-10)));
        log.Record(Run(TestData.FixedNow));

        Assert.Equal(2, log.All.Count);
        Assert.Equal(TestData.FixedNow, log.All[0].RanAt);
    }

    [Fact]
    public void The_change_log_is_bounded_so_it_cannot_grow_without_limit()
    {
        var log = new InMemoryChangeLog(limit: 3);

        for (var i = 0; i < 10; i++)
        {
            log.Record(Run(TestData.FixedNow.AddMinutes(i)));
        }

        Assert.Equal(3, log.All.Count);
    }

    [Fact]
    public void The_log_reports_which_changes_have_already_been_applied_to_an_account()
    {
        var log = new InMemoryChangeLog();
        var change = Proposal(ChangeRisk.Additive);

        log.Record(new ChangeRunResult
        {
            RunId = Guid.NewGuid(),
            RanAt = TestData.FixedNow,
            AccountId = change.AccountId,
            AccountName = "Test",
            WasPreview = false,
            Outcomes = [new ChangeOutcome(change, ChangeStatus.Applied, "done")],
        });

        Assert.Contains(change.Id, log.AppliedChangeIds(change.AccountId));
    }

    [Fact]
    public void A_preview_does_not_count_as_applied()
    {
        var log = new InMemoryChangeLog();
        var change = Proposal(ChangeRisk.Additive);

        log.Record(new ChangeRunResult
        {
            RunId = Guid.NewGuid(),
            RanAt = TestData.FixedNow,
            AccountId = change.AccountId,
            AccountName = "Test",
            WasPreview = true,
            Outcomes = [new ChangeOutcome(change, ChangeStatus.Previewed, "would do")],
        });

        // Otherwise reviewing a change would remove it from the list without applying it.
        Assert.Empty(log.AppliedChangeIds(change.AccountId));
    }

    [Fact]
    public void A_failed_change_stays_available_to_retry()
    {
        var log = new InMemoryChangeLog();
        var change = Proposal(ChangeRisk.Additive);

        log.Record(new ChangeRunResult
        {
            RunId = Guid.NewGuid(),
            RanAt = TestData.FixedNow,
            AccountId = change.AccountId,
            AccountName = "Test",
            WasPreview = false,
            Outcomes = [new ChangeOutcome(change, ChangeStatus.Failed, "rejected")],
        });

        Assert.Empty(log.AppliedChangeIds(change.AccountId));
    }

    [Fact]
    public void Applied_ids_are_scoped_to_their_own_account()
    {
        var log = new InMemoryChangeLog();
        var change = Proposal(ChangeRisk.Additive);

        log.Record(new ChangeRunResult
        {
            RunId = Guid.NewGuid(),
            RanAt = TestData.FixedNow,
            AccountId = change.AccountId,
            AccountName = "Test",
            WasPreview = false,
            Outcomes = [new ChangeOutcome(change, ChangeStatus.Applied, "done")],
        });

        Assert.Empty(log.AppliedChangeIds(accountId: 999));
    }

    [Fact]
    public void The_preview_and_the_real_call_describe_the_operation_identically()
    {
        // Both appliers share one description, so a preview can never say something different
        // from what is actually sent.
        var change = Proposal(ChangeRisk.Additive);

        Assert.Contains("AddNegativeKeywordsToEntities", ChangeOperation.Describe(change));
        Assert.Contains(change.Value!, ChangeOperation.Describe(change));
    }

    private static ProposedChange Proposal(ChangeRisk risk) => new()
    {
        Id = "test-1",
        Kind = risk == ChangeRisk.Additive
            ? ChangeActionKind.AddCampaignNegativeKeyword
            : ChangeActionKind.PauseCampaign,
        Risk = risk,
        AccountId = 140100001,
        CustomerId = 9900001,
        TargetEntityId = 1004,
        TargetDescription = "Campaign: Test",
        Value = "wasteful query",
        MatchType = "Exact",
        Summary = "Add “wasteful query” as an exact-match negative keyword",
        Rationale = "Spent with no conversions.",
        Source = "Search term waste analysis",
    };

    private static ChangeRunResult Run(DateTimeOffset at) => new()
    {
        RunId = Guid.NewGuid(),
        RanAt = at,
        AccountId = 1,
        AccountName = "Test",
        WasPreview = false,
    };
}
