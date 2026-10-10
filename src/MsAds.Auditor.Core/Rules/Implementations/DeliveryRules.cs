using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Core.Rules.Implementations;

/// <summary>Flags a falling click-through rate, which usually points at relevance or competition.</summary>
public sealed class CtrDeclineRule(EntityScope scope) : EntityRule(scope)
{
    public override string RuleId => $"{ScopeSlug}-ctr-decline";

    public override string Name => $"{ScopeLabel}s with a declining click-through rate";

    public override string Description =>
        "Flags entities whose CTR fell by more than the configured decline percentage, where both "
        + "periods carry enough impressions for the movement to be meaningful.";

    public override IEnumerable<Finding> Evaluate(AuditContext context)
    {
        var settings = context.Settings;
        var formatter = context.Formatter;

        foreach (var entity in Entities(context).Where(e => e.HasComparison))
        {
            var ctr = entity.Delta(MetricKey.ClickThroughRate);

            // Both periods need volume: CTR on a handful of impressions swings wildly.
            if (entity.Current.Impressions < settings.MinimumImpressionsForCtr
                || (entity.Previous?.Impressions ?? 0) < settings.MinimumImpressionsForCtr)
            {
                continue;
            }

            if (ctr.PercentChange is null || ctr.PercentChange.Value > -settings.CtrDeclinePercent)
            {
                continue;
            }

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{entity.Name}: click-through rate down {formatter.FormatChange(ctr)}",
                Severity = FindingSeverity.Warning,
                Confidence = entity.Current.Impressions >= settings.MinimumImpressionsForCtr * 4
                    ? FindingConfidence.High
                    : FindingConfidence.Medium,
                EntityType = ScopeLabel,
                EntityName = entity.Name,
                EntityId = entity.EntityId,
                EntityContext = entity.Context,
                Category = FindingCategories.Relevance,
                SpendAtStake = entity.Current.Spend,
                Reason =
                    $"CTR fell from {formatter.FormatPercent(ctr.Previous)} to {formatter.FormatPercent(ctr.Current)} "
                    + $"({formatter.FormatChange(ctr)}), on {formatter.FormatInteger(entity.Current.Impressions)} "
                    + "impressions, which is beyond the configured decline threshold of "
                    + $"{settings.CtrDeclinePercent:N0}%.",
                SuggestedAction =
                    "Review ad relevance and the queries being matched. A falling CTR alongside rising impressions "
                    + "often means broader matching is pulling in less relevant traffic; check the search terms "
                    + "report and consider tightening match types or adding negatives.",
                Caveats =
                [
                    "A CTR decline can also reflect increased competition, seasonality, or a shift in the mix of "
                    + "networks and devices serving the ads, rather than a problem with the ads themselves.",
                ],
                Evidence = RuleEvidence.For(formatter, entity,
                    MetricKey.ClickThroughRate, MetricKey.Impressions, MetricKey.Clicks, MetricKey.AverageCpc),
            };
        }
    }
}

/// <summary>
/// Flags entities that have stopped delivering, or whose delivery changed sharply. A campaign
/// that silently stopped serving is invisible in totals but important.
/// </summary>
public sealed class DeliveryChangeRule(EntityScope scope) : EntityRule(scope)
{
    public override string RuleId => $"{ScopeSlug}-delivery-change";

    public override string Name => $"{ScopeLabel}s that stopped or dropped delivery";

    public override string Description =>
        "Flags entities that delivered impressions in the previous period but none in the current "
        + "one, or whose impressions fell by more than the configured delivery-drop percentage.";

    public override IEnumerable<Finding> Evaluate(AuditContext context)
    {
        var settings = context.Settings;
        var formatter = context.Formatter;

        foreach (var entity in Entities(context).Where(e => e.HasComparison))
        {
            var previousImpressions = entity.Previous!.Impressions;

            if (previousImpressions < settings.MinimumImpressionsForCtr)
            {
                continue;
            }

            var impressions = entity.Delta(MetricKey.Impressions);
            var stopped = entity.Current.Impressions == 0;
            var dropped = impressions.PercentChange is { } change && change <= -settings.DeliveryDropPercent;

            if (!stopped && !dropped)
            {
                continue;
            }

            var statusNote = string.IsNullOrWhiteSpace(entity.Status)
                ? "The current status was not reported."
                : $"Current status is reported as {entity.Status}.";

            yield return new Finding
            {
                RuleId = RuleId,
                Title = stopped
                    ? $"{entity.Name}: stopped delivering"
                    : $"{entity.Name}: impressions down {formatter.FormatChange(impressions)}",
                Severity = stopped ? FindingSeverity.Warning : FindingSeverity.Information,
                Confidence = FindingConfidence.High,
                EntityType = ScopeLabel,
                EntityName = entity.Name,
                EntityId = entity.EntityId,
                EntityContext = entity.Context,
                Category = FindingCategories.Delivery,
                SpendAtStake = entity.Previous.Spend,
                Reason = stopped
                    ? $"Delivered {formatter.FormatInteger(previousImpressions)} impressions in the previous period "
                      + $"and none in the current one. {statusNote}"
                    : $"Impressions fell from {formatter.FormatInteger(impressions.Previous)} to "
                      + $"{formatter.FormatInteger(impressions.Current)} ({formatter.FormatChange(impressions)}), "
                      + $"beyond the configured threshold of {settings.DeliveryDropPercent:N0}%. {statusNote}",
                SuggestedAction = stopped
                    ? "Check whether this was paused deliberately, has exhausted its budget, or has lost eligibility "
                      + "(for example disapproved ads or an ended schedule). Review the change history for a status "
                      + "or budget change."
                    : "Check budget, bids and impression share lost to budget and rank, and review the change history "
                      + "for changes in this period.",
                Caveats =
                [
                    "A deliberate pause or an intentional budget reduction will appear here exactly like an "
                    + "accidental one. This finding reports the delivery change, not its cause.",
                ],
                Evidence = RuleEvidence.For(formatter, entity,
                    MetricKey.Impressions, MetricKey.Clicks, MetricKey.Spend, MetricKey.Conversions),
            };
        }
    }
}

/// <summary>
/// Flags campaigns losing impression share to budget, where that data is available. Campaign
/// scope only — impression share lost to budget is not a keyword-level concept.
/// </summary>
public sealed class ImpressionShareLostToBudgetRule : IAuditRule
{
    public string RuleId => "campaign-impression-share-lost-to-budget";

    public string Name => "Campaigns constrained by budget";

    public string Description =>
        "Flags campaigns whose impression share lost to budget exceeds the configured percentage. "
        + "Inactive where the account does not return impression share data.";

    public IEnumerable<Finding> Evaluate(AuditContext context)
    {
        var settings = context.Settings;
        var formatter = context.Formatter;

        foreach (var campaign in context.Campaigns)
        {
            var lostToBudget = campaign.ImpressionShareLostToBudget;

            if (lostToBudget is null || lostToBudget.Value < settings.ImpressionShareLostToBudgetPercent)
            {
                continue;
            }

            var converting = campaign.Current.Conversions is > 0m;

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{campaign.Name}: losing {formatter.FormatPercent(lostToBudget)} of impression share to budget",
                Severity = converting ? FindingSeverity.Warning : FindingSeverity.Information,
                Confidence = FindingConfidence.High,
                EntityType = "Campaign",
                EntityName = campaign.Name,
                EntityId = campaign.EntityId,
                Category = converting ? FindingCategories.Opportunity : FindingCategories.Delivery,
                SpendAtStake = campaign.Current.Spend,
                Reason =
                    $"Impression share lost to budget is {formatter.FormatPercent(lostToBudget)}, above the "
                    + $"configured threshold of {settings.ImpressionShareLostToBudgetPercent:N0}%. Impression share "
                    + $"is {formatter.FormatPercent(campaign.ImpressionShare)}. "
                    + (converting
                        ? "This campaign is recording conversions, so the lost share represents demand it could "
                          + "plausibly have converted."
                        : "This campaign is not currently recording conversions, so raising its budget is not "
                          + "obviously the right response."),
                SuggestedAction = converting
                    ? "Consider whether budget can be increased or reallocated from campaigns with a worse CPA. "
                      + "Confirm the campaign's CPA is acceptable at current volume before scaling it."
                    : "Resolve the conversion performance before adding budget. A budget-constrained campaign that "
                      + "does not convert will simply spend more without return.",
                Caveats =
                [
                    "Impression share is an estimate produced by Microsoft Advertising, not a measured count.",
                    "Impression share lost to budget does not say that the missed impressions would have converted "
                    + "at the same rate as those served.",
                ],
                Evidence =
                [
                    new Evidence("Impression share", formatter.FormatPercent(campaign.ImpressionShare)),
                    new Evidence("Lost to budget", formatter.FormatPercent(lostToBudget)),
                    new Evidence("Lost to rank", formatter.FormatPercent(campaign.ImpressionShareLostToRank)),
                    RuleEvidence.For(formatter, campaign, MetricKey.Spend),
                    RuleEvidence.For(formatter, campaign, MetricKey.Conversions),
                    RuleEvidence.For(formatter, campaign, MetricKey.CostPerAcquisition),
                ],
            };
        }
    }
}

/// <summary>
/// Flags campaigns whose CPA is far worse than the account as a whole. Used when no target CPA is
/// configured, since the account average is the only available yardstick, but reported as a
/// relative observation rather than a failure.
/// </summary>
public sealed class CampaignVersusAccountAverageRule : IAuditRule
{
    public string RuleId => "campaign-cpa-versus-account-average";

    public string Name => "Campaigns well above the account average CPA";

    public string Description =>
        "Flags campaigns whose CPA exceeds the account average by the configured multiple, with "
        + "enough spend and conversions to be worth comparing.";

    public IEnumerable<Finding> Evaluate(AuditContext context)
    {
        if (context.ConversionDataUnavailable)
        {
            yield break;
        }

        var settings = context.Settings;
        var formatter = context.Formatter;
        var accountCpa = context.AccountCpa;

        if (accountCpa is null or <= 0m)
        {
            yield break;
        }

        var threshold = accountCpa.Value * settings.CpaVersusAccountMultiple;

        foreach (var campaign in context.Campaigns)
        {
            var cpa = campaign.Current.CostPerAcquisition;

            if (cpa is null || cpa.Value <= threshold || campaign.Current.Spend < settings.MinimumSpend)
            {
                continue;
            }

            var conversions = campaign.Current.Conversions ?? 0m;

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{campaign.Name}: CPA is {formatter.Format(cpa!.Value / accountCpa.Value, MetricFormat.Ratio)} the account average",
                Severity = FindingSeverity.Warning,
                Confidence = conversions >= settings.MinimumConversions
                    ? FindingConfidence.Medium
                    : FindingConfidence.Low,
                EntityType = "Campaign",
                EntityName = campaign.Name,
                EntityId = campaign.EntityId,
                Category = FindingCategories.Efficiency,
                SpendAtStake = campaign.Current.Spend,
                Reason =
                    $"This campaign's CPA of {formatter.FormatCurrency(cpa)} is more than "
                    + $"{settings.CpaVersusAccountMultiple:N1}× the account average of "
                    + $"{formatter.FormatCurrency(accountCpa)}, on {formatter.FormatCurrency(campaign.Current.Spend)} "
                    + "of spend.",
                SuggestedAction =
                    "Compare this campaign's objective with the rest of the account before acting. If it is pursuing "
                    + "the same goal as the others, review its bids, targeting and search terms; if it serves a "
                    + "different purpose, such as prospecting or brand defence, a higher CPA may be expected.",
                Caveats =
                [
                    "The account average is not a target. Campaigns legitimately differ in objective, audience and "
                    + "funnel position, so a higher CPA is not automatically a fault.",
                    RuleEvidence.AttributionCaveat(),
                ],
                Evidence = RuleEvidence.For(formatter, campaign,
                    MetricKey.Spend, MetricKey.Conversions, MetricKey.CostPerAcquisition, MetricKey.ConversionRate),
            };
        }
    }
}

/// <summary>Flags keywords whose cost per click rose sharply, which drives CPA up even at constant conversion rates.</summary>
public sealed class CpcIncreaseRule(EntityScope scope) : EntityRule(scope)
{
    public override string RuleId => $"{ScopeSlug}-cpc-increase";

    public override string Name => $"{ScopeLabel}s with a sharply rising cost per click";

    public override string Description =>
        "Flags entities whose average CPC rose by more than the significant-change percentage, "
        + "with enough clicks in both periods to be meaningful.";

    public override IEnumerable<Finding> Evaluate(AuditContext context)
    {
        var settings = context.Settings;
        var formatter = context.Formatter;

        foreach (var entity in Entities(context).Where(e => e.HasComparison))
        {
            if (entity.Current.Clicks < settings.MinimumClicks
                || (entity.Previous?.Clicks ?? 0) < settings.MinimumClicks)
            {
                continue;
            }

            var cpc = entity.Delta(MetricKey.AverageCpc);

            if (cpc.PercentChange is null || cpc.PercentChange.Value < settings.SignificantChangePercent)
            {
                continue;
            }

            var cpa = entity.Delta(MetricKey.CostPerAcquisition);

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{entity.Name}: average CPC up {formatter.FormatChange(cpc)}",
                Severity = FindingSeverity.Warning,
                Confidence = FindingConfidence.Medium,
                EntityType = ScopeLabel,
                EntityName = entity.Name,
                EntityId = entity.EntityId,
                EntityContext = entity.Context,
                Category = FindingCategories.Efficiency,
                SpendAtStake = entity.Current.Spend,
                Reason =
                    $"Average CPC rose from {formatter.FormatCurrency(cpc.Previous)} to "
                    + $"{formatter.FormatCurrency(cpc.Current)} ({formatter.FormatChange(cpc)}). CPA moved "
                    + $"{formatter.FormatChange(cpa)} over the same period.",
                SuggestedAction =
                    "Check for bid or bid-strategy changes in the change history, and review whether increased "
                    + "competition or a shift towards broader match types is responsible. A rising CPC is acceptable "
                    + "if conversion value rose with it.",
                Caveats =
                [
                    "A rising CPC can be a deliberate consequence of bidding more aggressively for higher-value "
                    + "traffic, and is only a problem if CPA or ROAS worsened.",
                ],
                Evidence = RuleEvidence.For(formatter, entity,
                    MetricKey.AverageCpc, MetricKey.Clicks, MetricKey.Spend, MetricKey.CostPerAcquisition, MetricKey.Conversions),
            };
        }
    }
}
