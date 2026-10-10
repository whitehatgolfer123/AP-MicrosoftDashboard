using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Core.Rules.Implementations;

/// <summary>
/// Flags entities that spent meaningfully but recorded no conversions at all. Deliberately does
/// not call them bad: no conversions over a short period, or on thin traffic, is reported as a
/// low-confidence prompt to investigate.
/// </summary>
public sealed class SpendWithNoConversionsRule(EntityScope scope) : EntityRule(scope)
{
    public override string RuleId => $"{ScopeSlug}-zero-conversions";

    public override string Name => $"{ScopeLabel}s with spend but no conversions";

    public override string Description =>
        "Flags entities whose spend exceeds the minimum spend threshold while recording zero "
        + "conversions. Confidence is graded by click volume, and the finding is suppressed "
        + "entirely when conversion data is unavailable.";

    public override IEnumerable<Finding> Evaluate(AuditContext context)
    {
        // Without conversion data, zero conversions is unknowable rather than true.
        if (context.ConversionDataUnavailable)
        {
            yield break;
        }

        var settings = context.Settings;
        var formatter = context.Formatter;

        foreach (var entity in Entities(context).Where(e => e.Current.HasSpendButNoConversions))
        {
            if (entity.Current.Spend < settings.MinimumSpend)
            {
                continue;
            }

            var clicks = entity.Current.Clicks;
            var caveats = new List<string> { RuleEvidence.AttributionCaveat() };

            if (RuleEvidence.ThinDataCaveat(clicks, settings.MinimumClicks, context.Period.Current.Days) is { } thin)
            {
                caveats.Add(thin);
            }

            caveats.Add(
                "Zero recorded conversions can also mean conversion tracking is missing or "
                + "misconfigured for this traffic, rather than that the traffic is worthless.");

            var confidence = RuleEvidence.ConfidenceFromVolume(clicks, settings.MinimumClicks);

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{entity.Name}: {formatter.FormatCurrency(entity.Current.Spend)} spent with no recorded conversions",
                Severity = entity.Current.Spend >= settings.MinimumSpend * 3m && confidence != FindingConfidence.Low
                    ? FindingSeverity.Critical
                    : FindingSeverity.Warning,
                Confidence = confidence,
                EntityType = ScopeLabel,
                EntityName = entity.Name,
                EntityId = entity.EntityId,
                EntityContext = entity.Context,
                Category = FindingCategories.WastedSpend,
                SpendAtStake = entity.Current.Spend,
                Reason =
                    $"This {ScopeLabelLower} spent {formatter.FormatCurrency(entity.Current.Spend)} across "
                    + $"{clicks} click(s) in the reporting period and recorded no conversions. Spend above the "
                    + $"configured minimum of {formatter.FormatCurrency(settings.MinimumSpend)} with no "
                    + "conversions is the clearest candidate for wasted budget.",
                SuggestedAction =
                    "First confirm conversion tracking is firing for this traffic. If tracking is sound, "
                    + $"review the {ScopeLabelLower}'s search terms, match types and landing page relevance "
                    + "before deciding whether to reduce bids, add negative keywords or pause.",
                Caveats = caveats,
                Evidence = RuleEvidence.For(formatter, entity,
                    MetricKey.Spend, MetricKey.Clicks, MetricKey.Conversions, MetricKey.ClickThroughRate, MetricKey.AverageCpc),
            };
        }
    }
}

/// <summary>
/// Flags entities whose CPA exceeds the configured target by more than the allowed tolerance.
/// Runs only when a target CPA has been configured — without one the application does not claim
/// to know what good looks like.
/// </summary>
public sealed class CpaAboveTargetRule(EntityScope scope) : EntityRule(scope)
{
    public override string RuleId => $"{ScopeSlug}-cpa-above-target";

    public override string Name => $"{ScopeLabel}s with CPA above target";

    public override string Description =>
        "Flags entities whose cost per acquisition exceeds the configured target CPA by more than "
        + "the tolerance percentage. Inactive unless a target CPA is set.";

    public override IEnumerable<Finding> Evaluate(AuditContext context)
    {
        var settings = context.Settings;

        if (!settings.HasTargetCpa || context.ConversionDataUnavailable)
        {
            yield break;
        }

        var threshold = settings.CpaFlagThreshold!.Value;
        var target = settings.TargetCpa!.Value;
        var formatter = context.Formatter;

        foreach (var entity in Entities(context))
        {
            var cpa = entity.Current.CostPerAcquisition;

            if (cpa is null || cpa.Value <= threshold || entity.Current.Spend < settings.MinimumSpend)
            {
                continue;
            }

            var overBy = MetricMath.PercentChange(cpa, target);
            var conversions = entity.Current.Conversions ?? 0m;

            var caveats = new List<string> { RuleEvidence.AttributionCaveat() };

            if (conversions < settings.MinimumConversions)
            {
                caveats.Add(
                    $"Based on only {formatter.FormatInteger(conversions)} conversion(s), below the configured "
                    + $"minimum of {formatter.FormatInteger(settings.MinimumConversions)}. CPA is volatile at "
                    + "this volume.");
            }

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{entity.Name}: CPA of {formatter.FormatCurrency(cpa)} is above the {formatter.FormatCurrency(target)} target",
                Severity = FindingSeverity.Warning,
                Confidence = conversions >= settings.MinimumConversions
                    ? FindingConfidence.High
                    : conversions > 0m ? FindingConfidence.Medium : FindingConfidence.Low,
                EntityType = ScopeLabel,
                EntityName = entity.Name,
                EntityId = entity.EntityId,
                EntityContext = entity.Context,
                Category = FindingCategories.Efficiency,
                SpendAtStake = entity.Current.Spend,
                Reason =
                    $"CPA of {formatter.FormatCurrency(cpa)} is {formatter.FormatSignedPercent(overBy)} against the "
                    + $"target of {formatter.FormatCurrency(target)}, beyond the configured tolerance of "
                    + $"{settings.CpaToleranceAboveTargetPercent:N0}% (flagging above {formatter.FormatCurrency(threshold)}).",
                SuggestedAction =
                    $"Review bids and budget allocation for this {ScopeLabelLower}. Check whether a small number "
                    + "of expensive, non-converting search terms account for the gap before reducing spend across "
                    + "the board.",
                Caveats = caveats,
                Evidence = RuleEvidence.For(formatter, entity,
                    MetricKey.Spend, MetricKey.Conversions, MetricKey.CostPerAcquisition, MetricKey.ConversionRate, MetricKey.AverageCpc),
            };
        }
    }
}

/// <summary>
/// Flags entities where spend rose significantly while conversions fell — the pattern most often
/// worth investigating, because it points at a change rather than at steady-state inefficiency.
/// </summary>
public sealed class SpendUpConversionsDownRule(EntityScope scope) : EntityRule(scope)
{
    public override string RuleId => $"{ScopeSlug}-spend-up-conversions-down";

    public override string Name => $"{ScopeLabel}s with rising spend and falling conversions";

    public override string Description =>
        "Flags entities whose spend rose by more than the significant-change percentage while "
        + "conversions fell. Requires data for both periods.";

    public override IEnumerable<Finding> Evaluate(AuditContext context)
    {
        if (context.ConversionDataUnavailable)
        {
            yield break;
        }

        var settings = context.Settings;
        var formatter = context.Formatter;

        foreach (var entity in Entities(context).Where(e => e.HasComparison))
        {
            if (entity.Current.Spend < settings.MinimumSpend)
            {
                continue;
            }

            var spend = entity.Delta(MetricKey.Spend);
            var conversions = entity.Delta(MetricKey.Conversions);
            var cpa = entity.Delta(MetricKey.CostPerAcquisition);

            var spendRoseSignificantly = spend.PercentChange >= settings.SignificantChangePercent;
            var conversionsFell = conversions.Kind is ChangeKind.Decrease or ChangeKind.CeasedActivity;

            if (!spendRoseSignificantly || !conversionsFell)
            {
                continue;
            }

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{entity.Name}: spend up {formatter.FormatChange(spend)} while conversions fell {formatter.FormatChange(conversions)}",
                Severity = FindingSeverity.Critical,
                Confidence = RuleEvidence.ConfidenceFromVolume(entity.Current.Clicks, settings.MinimumClicks),
                EntityType = ScopeLabel,
                EntityName = entity.Name,
                EntityId = entity.EntityId,
                EntityContext = entity.Context,
                Category = FindingCategories.Efficiency,
                SpendAtStake = entity.Current.Spend,
                Reason =
                    $"Spend moved from {formatter.FormatCurrency(spend.Previous)} to "
                    + $"{formatter.FormatCurrency(spend.Current)} ({formatter.FormatChange(spend)}) while conversions "
                    + $"moved from {formatter.FormatInteger(conversions.Previous)} to "
                    + $"{formatter.FormatInteger(conversions.Current)} ({formatter.FormatChange(conversions)}). "
                    + $"CPA consequently moved {formatter.FormatChange(cpa)}.",
                SuggestedAction =
                    "Check the change history for budget, bid or targeting changes in this period, then review "
                    + "search terms for newly expensive queries. Confirm conversion tracking has not broken before "
                    + "concluding that performance genuinely declined.",
                Caveats =
                [
                    RuleEvidence.AttributionCaveat(),
                    "A fall in recorded conversions can be caused by a tracking fault or by attribution lag on "
                    + "recent dates, not only by a real decline.",
                ],
                Evidence = RuleEvidence.For(formatter, entity,
                    MetricKey.Spend, MetricKey.Conversions, MetricKey.CostPerAcquisition, MetricKey.Clicks, MetricKey.ConversionRate),
            };
        }
    }
}

/// <summary>
/// Flags strong performers so the report says what to do more of, not only what to cut. Requires
/// a genuine conversion signal before calling anything a success.
/// </summary>
public sealed class StrongPerformerRule(EntityScope scope) : EntityRule(scope)
{
    public override string RuleId => $"{ScopeSlug}-strong-performer";

    public override string Name => $"{ScopeLabel}s performing well enough to investigate scaling";

    public override string Description =>
        "Highlights entities meeting the minimum conversion count whose CPA beats target (or the "
        + "account average, when no target is set) or whose ROAS beats target.";

    public override IEnumerable<Finding> Evaluate(AuditContext context)
    {
        if (context.ConversionDataUnavailable)
        {
            yield break;
        }

        var settings = context.Settings;
        var formatter = context.Formatter;

        foreach (var entity in Entities(context))
        {
            var conversions = entity.Current.Conversions;

            if (conversions is null || conversions.Value < settings.MinimumConversions)
            {
                continue;
            }

            var cpa = entity.Current.CostPerAcquisition;
            var roas = entity.Current.ReturnOnAdSpend;

            var (qualifies, basis) = Assess(settings, context.AccountCpa, cpa, roas, formatter);

            if (!qualifies)
            {
                continue;
            }

            var caveats = new List<string> { RuleEvidence.AttributionCaveat() };

            if (entity.ImpressionShare is null && Scope == EntityScope.Campaign)
            {
                caveats.Add(
                    "Impression share data was not available, so there is no evidence here about how much "
                    + "additional volume is actually available to capture.");
            }
            else if (entity.ImpressionShare is { } share and > 85m)
            {
                caveats.Add(
                    $"Impression share is already {formatter.FormatPercent(share)}, so there may be limited "
                    + "headroom for additional volume.");
            }

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{entity.Name}: performing well on {basis}",
                Severity = FindingSeverity.Positive,
                Confidence = conversions.Value >= settings.MinimumConversions * 2m
                    ? FindingConfidence.High
                    : FindingConfidence.Medium,
                EntityType = ScopeLabel,
                EntityName = entity.Name,
                EntityId = entity.EntityId,
                EntityContext = entity.Context,
                Category = FindingCategories.Opportunity,
                SpendAtStake = entity.Current.Spend,
                Reason =
                    $"Recorded {formatter.FormatInteger(conversions)} conversion(s) from "
                    + $"{formatter.FormatCurrency(entity.Current.Spend)} of spend, which is enough volume for the "
                    + $"result to be meaningful, and it compares favourably on {basis}.",
                SuggestedAction =
                    "Investigate whether there is unmet demand here before increasing investment: check impression "
                    + "share lost to budget and to rank, and confirm the conversion quality is genuine rather than "
                    + "driven by a low-value goal.",
                Caveats = caveats,
                Evidence = RuleEvidence.For(formatter, entity,
                    MetricKey.Spend, MetricKey.Conversions, MetricKey.CostPerAcquisition, MetricKey.ReturnOnAdSpend, MetricKey.ConversionRate),
            };
        }
    }

    /// <summary>
    /// Decides whether performance is good, preferring explicit targets and falling back to the
    /// account's own average when none is configured.
    /// </summary>
    private static (bool Qualifies, string Basis) Assess(
        Settings.AuditSettings settings,
        decimal? accountCpa,
        decimal? cpa,
        decimal? roas,
        MetricFormatter formatter)
    {
        if (settings.HasTargetRoas && roas is not null && roas.Value >= settings.TargetRoas!.Value)
        {
            return (true, $"ROAS ({formatter.Format(roas, MetricKey.ReturnOnAdSpend)} against a "
                + $"{formatter.Format(settings.TargetRoas, MetricKey.ReturnOnAdSpend)} target)");
        }

        if (settings.HasTargetCpa && cpa is not null && cpa.Value <= settings.TargetCpa!.Value)
        {
            return (true, $"CPA ({formatter.FormatCurrency(cpa)} against a "
                + $"{formatter.FormatCurrency(settings.TargetCpa)} target)");
        }

        // No target configured: compare against the account's own average rather than invent one.
        if (!settings.HasTargetCpa && !settings.HasTargetRoas
            && cpa is not null && accountCpa is > 0m && cpa.Value <= accountCpa.Value * 0.8m)
        {
            return (true, $"CPA ({formatter.FormatCurrency(cpa)} against an account average of "
                + $"{formatter.FormatCurrency(accountCpa)}; no target CPA is configured, so this is a relative "
                + "observation only)");
        }

        return (false, string.Empty);
    }
}
