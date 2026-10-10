using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Core.Rules.Implementations;

/// <summary>
/// Reports the headline account movement: whether efficiency improved or worsened, and which
/// campaign contributed most to any spend increase. This is the finding the executive summary
/// leans on.
/// </summary>
public sealed class AccountEfficiencyRule : IAuditRule
{
    public string RuleId => "account-efficiency-shift";

    public string Name => "Account-level efficiency shift";

    public string Description =>
        "Compares account spend, conversions and CPA against the previous period and identifies "
        + "the campaign responsible for the largest share of any spend increase.";

    public IEnumerable<Finding> Evaluate(AuditContext context)
    {
        if (context.AccountPrevious is null)
        {
            yield break;
        }

        var settings = context.Settings;
        var formatter = context.Formatter;
        var current = context.AccountCurrent;
        var previous = context.AccountPrevious;

        var spend = MetricDelta.Create(current.Spend, previous.Spend, MetricPolarity.Neutral);
        var conversions = MetricDelta.Create(current.Conversions, previous.Conversions, MetricPolarity.HigherIsBetter);
        var cpa = MetricDelta.Create(current.CostPerAcquisition, previous.CostPerAcquisition, MetricPolarity.LowerIsBetter);

        var spendMovedMaterially = spend.PercentChange is { } s && Math.Abs(s) >= settings.SignificantChangePercent;
        var cpaMovedMaterially = cpa.PercentChange is { } c && Math.Abs(c) >= settings.SignificantChangePercent;

        if (!spendMovedMaterially && !cpaMovedMaterially)
        {
            yield break;
        }

        // Attribute the spend movement to the campaign that moved most, so the finding is actionable.
        var biggestMover = context.Campaigns
            .Where(c => c.HasComparison)
            .Select(c => (Campaign: c, Change: c.Current.Spend - c.Previous!.Spend))
            .OrderByDescending(x => Math.Abs(x.Change))
            .FirstOrDefault();

        var cpaWorsened = cpa.Verdict is Verdict.Negative;

        var reason =
            $"Account spend moved {formatter.FormatChange(spend)} (from {formatter.FormatCurrency(spend.Previous)} to "
            + $"{formatter.FormatCurrency(spend.Current)}) while conversions moved {formatter.FormatChange(conversions)}. "
            + $"CPA therefore moved {formatter.FormatChange(cpa)}, from {formatter.FormatCurrency(cpa.Previous)} to "
            + $"{formatter.FormatCurrency(cpa.Current)}.";

        if (biggestMover.Campaign is not null && biggestMover.Change != 0m)
        {
            var direction = biggestMover.Change > 0m ? "increase" : "decrease";
            reason += $" The largest single contributor to the spend {direction} was "
                + $"{biggestMover.Campaign.Name}, at {formatter.FormatCurrency(Math.Abs(biggestMover.Change))}.";
        }

        var caveats = new List<string> { RuleEvidence.AttributionCaveat() };

        if (context.Period.CurrentDayExcluded)
        {
            caveats.Add(
                $"Today was excluded from both periods, so the comparison covers two complete "
                + $"{context.Period.Current.Days}-day windows.");
        }

        yield return new Finding
        {
            RuleId = RuleId,
            Title = cpaWorsened
                ? $"Account CPA worsened by {formatter.FormatChange(cpa)}"
                : cpa.Verdict is Verdict.Positive
                    ? $"Account CPA improved by {formatter.FormatChange(cpa)}"
                    : $"Account spend moved {formatter.FormatChange(spend)}",
            Severity = cpaWorsened ? FindingSeverity.Warning
                : cpa.Verdict is Verdict.Positive ? FindingSeverity.Positive
                : FindingSeverity.Information,
            Confidence = FindingConfidence.High,
            EntityType = "Account",
            EntityName = $"Account {context.Scope.AccountId}",
            EntityId = context.Scope.AccountId,
            Category = FindingCategories.Efficiency,
            SpendAtStake = current.Spend,
            Reason = reason,
            SuggestedAction = cpaWorsened
                ? "Work down from the campaign contributing most to the spend increase: review its search terms for "
                  + "newly expensive queries, and check the change history for budget or bidding changes in this period."
                : "No corrective action is implied. Record what changed so the improvement can be repeated, and check "
                  + "whether the better CPA came with lower volume.",
            Caveats = caveats,
            Evidence =
            [
                new Evidence("Spend", formatter.FormatCurrency(spend.Current), formatter.FormatCurrency(spend.Previous), formatter.FormatChange(spend)),
                new Evidence("Conversions", formatter.FormatInteger(conversions.Current), formatter.FormatInteger(conversions.Previous), formatter.FormatChange(conversions)),
                new Evidence("CPA", formatter.FormatCurrency(cpa.Current), formatter.FormatCurrency(cpa.Previous), formatter.FormatChange(cpa)),
            ],
        };
    }
}

/// <summary>
/// Raises a tracking concern when the account took meaningful paid traffic but recorded no
/// conversions at all, or when conversion data could not be retrieved. Without this, every other
/// conversion-based conclusion in the report would rest on an unexamined assumption.
/// </summary>
public sealed class ConversionTrackingRule : IAuditRule
{
    public string RuleId => "account-conversion-tracking";

    public string Name => "Conversion tracking sanity check";

    public string Description =>
        "Warns when conversion data is unavailable, or when the account recorded clicks and spend "
        + "across the whole period without a single conversion.";

    public IEnumerable<Finding> Evaluate(AuditContext context)
    {
        var formatter = context.Formatter;
        var current = context.AccountCurrent;

        if (context.ConversionDataUnavailable)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Title = "Conversion data was not available for this account",
                Severity = FindingSeverity.Warning,
                Confidence = FindingConfidence.High,
                EntityType = "Account",
                EntityName = $"Account {context.Scope.AccountId}",
                EntityId = context.Scope.AccountId,
                Category = FindingCategories.Tracking,
                Reason =
                    "The reports returned no conversion columns, so conversions, conversion rate, CPA and ROAS "
                    + "cannot be calculated. Every rule that depends on conversion data has been suppressed rather "
                    + "than treating the absence as zero.",
                SuggestedAction =
                    "Check that conversion goals are configured for this account and that the reporting user has "
                    + "permission to see them. Until then, treat this report as covering cost and traffic only.",
                Caveats = ["This is a statement about the data retrieved, not about the account's actual performance."],
                Evidence =
                [
                    new Evidence("Spend", formatter.FormatCurrency(current.Spend)),
                    new Evidence("Clicks", formatter.FormatInteger(current.Clicks)),
                    new Evidence("Conversions", MetricFormatter.NotAvailable),
                ],
            };

            yield break;
        }

        // Clicks and spend with a genuine zero across the whole account usually means broken tracking.
        if (current.Clicks >= context.Settings.MinimumClicks
            && current.Spend >= context.Settings.MinimumSpend
            && current.Conversions == 0m)
        {
            yield return new Finding
            {
                RuleId = RuleId + "-zero",
                Title = "The account recorded no conversions at all in this period",
                Severity = FindingSeverity.Critical,
                Confidence = FindingConfidence.High,
                EntityType = "Account",
                EntityName = $"Account {context.Scope.AccountId}",
                EntityId = context.Scope.AccountId,
                Category = FindingCategories.Tracking,
                SpendAtStake = current.Spend,
                Reason =
                    $"The account spent {formatter.FormatCurrency(current.Spend)} across "
                    + $"{formatter.FormatInteger(current.Clicks)} clicks over "
                    + $"{context.Period.Current.Days} days and recorded zero conversions. An account-wide zero is "
                    + "more often a tracking fault than a genuine result.",
                SuggestedAction =
                    "Verify the Universal Event Tracking tag is present and firing on the site, and that at least one "
                    + "conversion goal is active and counted in reporting. Resolve this before acting on any "
                    + "conversion-based recommendation in this report.",
                Caveats =
                [
                    "A genuinely zero result is possible for a new account, a long sales cycle, or offline "
                    + "conversions that are never imported.",
                ],
                Evidence =
                [
                    new Evidence("Spend", formatter.FormatCurrency(current.Spend)),
                    new Evidence("Clicks", formatter.FormatInteger(current.Clicks)),
                    new Evidence("Conversions", formatter.FormatInteger(0m)),
                ],
            };
        }
    }
}
