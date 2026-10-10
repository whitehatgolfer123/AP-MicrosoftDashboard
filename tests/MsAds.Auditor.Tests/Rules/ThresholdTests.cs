using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Rules.Implementations;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Tests.Support;

namespace MsAds.Auditor.Tests.Rules;

/// <summary>
/// Each rule's threshold behaviour, including the cases where the rule must stay silent: thin
/// data, missing conversion data, and no configured target.
/// </summary>
public class ThresholdTests
{
    private static MetricSet NoConversions(decimal spend, long clicks)
        => TestData.Metrics(impressions: clicks * 20, clicks: clicks, spend: spend, conversions: 0m, conversionValue: 0m);

    [Fact]
    public void Zero_conversion_spend_is_flagged_once_spend_passes_the_minimum()
    {
        var context = TestData.Context(
            campaigns: [TestData.Campaign(current: NoConversions(120m, 60))],
            settings: new AuditSettings { MinimumSpend = 50m, MinimumClicks = 20 });

        var findings = new SpendWithNoConversionsRule(EntityScope.Campaign).Evaluate(context).ToList();

        var finding = Assert.Single(findings);
        Assert.Equal(FindingCategories.WastedSpend, finding.Category);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Equal(120m, finding.SpendAtStake);
    }

    [Fact]
    public void Zero_conversion_spend_below_the_minimum_is_not_flagged_at_all()
    {
        var context = TestData.Context(
            campaigns: [TestData.Campaign(current: NoConversions(30m, 15))],
            settings: new AuditSettings { MinimumSpend = 50m });

        Assert.Empty(new SpendWithNoConversionsRule(EntityScope.Campaign).Evaluate(context));
    }

    [Fact]
    public void A_campaign_with_few_clicks_is_flagged_only_with_low_confidence_and_a_caveat()
    {
        var context = TestData.Context(
            campaigns: [TestData.Campaign(current: NoConversions(80m, 5))],
            settings: new AuditSettings { MinimumSpend = 50m, MinimumClicks = 20 });

        var finding = Assert.Single(new SpendWithNoConversionsRule(EntityScope.Campaign).Evaluate(context));

        Assert.Equal(FindingConfidence.Low, finding.Confidence);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Contains(finding.Caveats, c => c.Contains("not enough traffic"));
    }

    [Fact]
    public void Nothing_is_flagged_as_zero_conversions_when_conversion_data_is_unavailable()
    {
        var context = TestData.Context(
            campaigns: [TestData.Campaign(current: TestData.Metrics(spend: 500m, clicks: 200, conversions: null))],
            conversionDataUnavailable: true);

        Assert.Empty(new SpendWithNoConversionsRule(EntityScope.Campaign).Evaluate(context));
    }

    [Fact]
    public void Cpa_above_target_fires_only_beyond_the_tolerance()
    {
        var settings = new AuditSettings
        {
            TargetCpa = 50m,
            CpaToleranceAboveTargetPercent = 20m,
            MinimumSpend = 50m,
        };

        // CPA of 55 is above target but inside the 20% tolerance (threshold 60).
        var withinTolerance = TestData.Context(
            campaigns: [TestData.Campaign(current: TestData.Metrics(spend: 550m, clicks: 200, conversions: 10m))],
            settings: settings);

        Assert.Empty(new CpaAboveTargetRule(EntityScope.Campaign).Evaluate(withinTolerance));

        // CPA of 70 is beyond it.
        var beyondTolerance = TestData.Context(
            campaigns: [TestData.Campaign(current: TestData.Metrics(spend: 700m, clicks: 200, conversions: 10m))],
            settings: settings);

        Assert.Single(new CpaAboveTargetRule(EntityScope.Campaign).Evaluate(beyondTolerance));
    }

    [Fact]
    public void Cpa_above_target_stays_silent_when_no_target_is_configured()
    {
        var context = TestData.Context(
            campaigns: [TestData.Campaign(current: TestData.Metrics(spend: 5000m, clicks: 200, conversions: 1m))],
            settings: new AuditSettings { TargetCpa = null });

        Assert.Empty(new CpaAboveTargetRule(EntityScope.Campaign).Evaluate(context));
    }

    [Fact]
    public void Rising_spend_with_falling_conversions_is_flagged_as_critical()
    {
        var context = TestData.Context(
            campaigns:
            [
                TestData.Campaign(
                    current: TestData.Metrics(spend: 1500m, clicks: 500, conversions: 10m),
                    previous: TestData.Metrics(spend: 1000m, clicks: 400, conversions: 20m)),
            ],
            settings: new AuditSettings { MinimumSpend = 50m, SignificantChangePercent = 20m });

        var finding = Assert.Single(new SpendUpConversionsDownRule(EntityScope.Campaign).Evaluate(context));

        Assert.Equal(FindingSeverity.Critical, finding.Severity);
        Assert.Contains("CPA", finding.Reason);
    }

    [Fact]
    public void Rising_spend_with_rising_conversions_is_not_flagged()
    {
        var context = TestData.Context(
            campaigns:
            [
                TestData.Campaign(
                    current: TestData.Metrics(spend: 1500m, clicks: 500, conversions: 30m),
                    previous: TestData.Metrics(spend: 1000m, clicks: 400, conversions: 20m)),
            ]);

        Assert.Empty(new SpendUpConversionsDownRule(EntityScope.Campaign).Evaluate(context));
    }

    [Fact]
    public void Ctr_decline_requires_enough_impressions_in_both_periods()
    {
        var settings = new AuditSettings { MinimumImpressionsForCtr = 500, CtrDeclinePercent = 20m };

        // CTR halves, but on only 100 impressions, so the movement is treated as noise.
        var thin = TestData.Context(
            campaigns:
            [
                TestData.Campaign(
                    current: TestData.Metrics(impressions: 100, clicks: 2),
                    previous: TestData.Metrics(impressions: 100, clicks: 4)),
            ],
            settings: settings);

        Assert.Empty(new CtrDeclineRule(EntityScope.Campaign).Evaluate(thin));

        var substantial = TestData.Context(
            campaigns:
            [
                TestData.Campaign(
                    current: TestData.Metrics(impressions: 10_000, clicks: 200),
                    previous: TestData.Metrics(impressions: 10_000, clicks: 400)),
            ],
            settings: settings);

        var finding = Assert.Single(new CtrDeclineRule(EntityScope.Campaign).Evaluate(substantial));
        Assert.Equal(FindingCategories.Relevance, finding.Category);
    }

    [Fact]
    public void A_campaign_that_stopped_delivering_is_reported()
    {
        var context = TestData.Context(
            campaigns:
            [
                TestData.Campaign(
                    status: "Paused",
                    current: new MetricSet { Conversions = 0m },
                    previous: TestData.Metrics(impressions: 20_000, clicks: 500, spend: 400m)),
            ]);

        var finding = Assert.Single(new DeliveryChangeRule(EntityScope.Campaign).Evaluate(context));

        Assert.Contains("stopped delivering", finding.Title);
        Assert.Equal(FindingCategories.Delivery, finding.Category);
        Assert.Contains("Paused", finding.Reason);
    }

    [Fact]
    public void Budget_constrained_campaigns_are_an_opportunity_when_converting_and_merely_informational_when_not()
    {
        var converting = TestData.Context(campaigns:
        [
            TestData.Campaign(current: TestData.Metrics(spend: 500m, conversions: 20m), lostToBudget: 35m, impressionShare: 40m),
        ]);

        var convertingFinding = Assert.Single(new ImpressionShareLostToBudgetRule().Evaluate(converting));
        Assert.Equal(FindingCategories.Opportunity, convertingFinding.Category);

        var notConverting = TestData.Context(campaigns:
        [
            TestData.Campaign(current: TestData.Metrics(spend: 500m, conversions: 0m), lostToBudget: 35m, impressionShare: 40m),
        ]);

        var otherFinding = Assert.Single(new ImpressionShareLostToBudgetRule().Evaluate(notConverting));
        Assert.Equal(FindingCategories.Delivery, otherFinding.Category);
        Assert.Contains("Resolve the conversion performance", otherFinding.SuggestedAction);
    }

    [Fact]
    public void Impression_share_rules_stay_silent_when_the_account_returns_no_impression_share_data()
    {
        var context = TestData.Context(campaigns: [TestData.Campaign(lostToBudget: null)]);

        Assert.Empty(new ImpressionShareLostToBudgetRule().Evaluate(context));
    }

    [Fact]
    public void A_strong_performer_needs_the_minimum_conversion_count_before_it_is_praised()
    {
        var settings = new AuditSettings { TargetCpa = 50m, MinimumConversions = 5m };

        var thin = TestData.Context(
            campaigns: [TestData.Campaign(current: TestData.Metrics(spend: 40m, clicks: 100, conversions: 2m))],
            settings: settings);

        Assert.Empty(new StrongPerformerRule(EntityScope.Campaign).Evaluate(thin));

        var solid = TestData.Context(
            campaigns: [TestData.Campaign(current: TestData.Metrics(spend: 300m, clicks: 400, conversions: 12m))],
            settings: settings);

        var finding = Assert.Single(new StrongPerformerRule(EntityScope.Campaign).Evaluate(solid));
        Assert.Equal(FindingSeverity.Positive, finding.Severity);
    }

    [Fact]
    public void Without_targets_a_strong_performer_is_judged_against_the_account_average_and_says_so()
    {
        var context = TestData.Context(
            accountCurrent: TestData.Metrics(spend: 1000m, clicks: 500, conversions: 10m), // account CPA 100
            campaigns: [TestData.Campaign(current: TestData.Metrics(spend: 300m, clicks: 200, conversions: 10m))], // CPA 30
            settings: new AuditSettings { TargetCpa = null, TargetRoas = null, MinimumConversions = 5m });

        var finding = Assert.Single(new StrongPerformerRule(EntityScope.Campaign).Evaluate(context));

        Assert.Contains("no target CPA is configured", finding.Reason);
    }

    [Fact]
    public void Cpc_increase_requires_the_click_minimum_in_both_periods()
    {
        var settings = new AuditSettings { MinimumClicks = 20, SignificantChangePercent = 20m };

        var context = TestData.Context(
            keywords:
            [
                new ComparedEntity
                {
                    EntityType = "Keyword",
                    EntityId = 1,
                    Name = "test [Exact]",
                    Current = TestData.Metrics(clicks: 100, spend: 300m),
                    Previous = TestData.Metrics(clicks: 100, spend: 200m),
                },
            ],
            settings: settings);

        var finding = Assert.Single(new CpcIncreaseRule(EntityScope.Keyword).Evaluate(context));
        Assert.Contains("average CPC up", finding.Title);
    }

    [Fact]
    public void The_conversion_tracking_rule_warns_when_the_whole_account_recorded_nothing()
    {
        var context = TestData.Context(
            accountCurrent: TestData.Metrics(spend: 900m, clicks: 400, conversions: 0m, conversionValue: 0m));

        var finding = Assert.Single(new ConversionTrackingRule().Evaluate(context));

        Assert.Equal(FindingSeverity.Critical, finding.Severity);
        Assert.Equal(FindingCategories.Tracking, finding.Category);
    }

    [Fact]
    public void The_conversion_tracking_rule_explains_itself_when_the_data_was_simply_unavailable()
    {
        var context = TestData.Context(
            accountCurrent: TestData.Metrics(spend: 900m, clicks: 400, conversions: null),
            conversionDataUnavailable: true);

        var finding = Assert.Single(new ConversionTrackingRule().Evaluate(context));

        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Contains("suppressed rather than treating the absence as zero", finding.Reason);
    }

    [Fact]
    public void Negative_keyword_conflicts_are_grouped_by_campaign()
    {
        var context = TestData.Context(conflicts:
        [
            new() { CampaignId = 1, CampaignName = "Alpha", Keyword = "a", NegativeKeyword = "x", ConflictLevel = "Campaign" },
            new() { CampaignId = 1, CampaignName = "Alpha", Keyword = "b", NegativeKeyword = "y", ConflictLevel = "Ad group" },
            new() { CampaignId = 2, CampaignName = "Beta", Keyword = "c", NegativeKeyword = "z", ConflictLevel = "Campaign" },
        ]);

        var findings = new NegativeKeywordConflictRule().Evaluate(context).ToList();

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.EntityName == "Alpha" && f.Title.Contains("2 keyword(s)"));
    }
}
