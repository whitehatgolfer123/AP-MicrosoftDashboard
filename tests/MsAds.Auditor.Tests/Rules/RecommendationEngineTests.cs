using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Tests.Support;

namespace MsAds.Auditor.Tests.Rules;

public class RecommendationEngineTests
{
    private sealed class ThrowingRule : IAuditRule
    {
        public string RuleId => "throwing";
        public string Name => "Throwing rule";
        public string Description => "Always fails, to prove failures are isolated.";
        public IEnumerable<Finding> Evaluate(AuditContext context) => throw new InvalidOperationException("boom");
    }

    private sealed class StubRule(FindingSeverity severity, decimal spend) : IAuditRule
    {
        public string RuleId => $"stub-{severity}-{spend}";
        public string Name => "Stub";
        public string Description => "Emits one finding.";

        public IEnumerable<Finding> Evaluate(AuditContext context) =>
        [
            new()
            {
                RuleId = RuleId,
                Title = $"{severity} {spend}",
                Severity = severity,
                Confidence = FindingConfidence.High,
                EntityType = "Campaign",
                Reason = "Stub",
                SuggestedAction = "Stub",
                Category = "Stub",
                SpendAtStake = spend,
            },
        ];
    }

    [Fact]
    public void A_failing_rule_does_not_lose_the_report_and_is_reported_as_a_failure()
    {
        var engine = new RecommendationEngine([new ThrowingRule(), new StubRule(FindingSeverity.Warning, 10m)]);

        var result = engine.Evaluate(TestData.Context());

        Assert.Single(result.Findings);
        Assert.Single(result.RuleFailures);
        Assert.Contains("boom", result.RuleFailures[0]);
    }

    [Fact]
    public void Findings_are_ordered_by_severity_then_by_the_spend_at_stake()
    {
        var engine = new RecommendationEngine(
        [
            new StubRule(FindingSeverity.Information, 1000m),
            new StubRule(FindingSeverity.Critical, 50m),
            new StubRule(FindingSeverity.Critical, 500m),
            new StubRule(FindingSeverity.Warning, 900m),
        ]);

        var result = engine.Evaluate(TestData.Context());

        Assert.Equal(
            ["Critical 500", "Critical 50", "Warning 900", "Information 1000"],
            [.. result.Findings.Select(f => f.Title)]);
    }

    [Fact]
    public void Every_registered_rule_is_constructible_and_has_a_distinct_identifier()
    {
        var engine = TestData.Engine();
        var ids = engine.Rules.Select(r => r.RuleId).ToList();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(engine.Rules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Name));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
        });
    }

    [Fact]
    public void Every_finding_carries_the_five_things_the_brief_requires()
    {
        var context = TestData.Context(
            accountCurrent: TestData.Metrics(spend: 2000m, clicks: 800, conversions: 20m),
            accountPrevious: TestData.Metrics(spend: 1200m, clicks: 700, conversions: 30m),
            campaigns:
            [
                TestData.Campaign(
                    name: "Rising",
                    current: TestData.Metrics(spend: 900m, clicks: 300, conversions: 2m),
                    previous: TestData.Metrics(spend: 500m, clicks: 250, conversions: 12m)),
            ]);

        var result = TestData.Engine().Evaluate(context);

        Assert.NotEmpty(result.Findings);
        Assert.All(result.Findings, finding =>
        {
            Assert.False(string.IsNullOrWhiteSpace(finding.Title));       // the finding
            Assert.NotEmpty(finding.Evidence);                            // the supporting data
            Assert.False(string.IsNullOrWhiteSpace(finding.Reason));      // the reason
            Assert.False(string.IsNullOrWhiteSpace(finding.SuggestedAction)); // the next action
            Assert.False(string.IsNullOrWhiteSpace(finding.RuleId));
        });
    }
}
