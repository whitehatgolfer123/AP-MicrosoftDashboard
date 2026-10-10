using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;

namespace MsAds.Auditor.Core.Rules.Implementations;

/// <summary>
/// Looks for recorded changes that sit close in time to a shift in account performance. The
/// wording is deliberately associative, never causal: the change history says what was altered
/// and when, and nothing in the API establishes that a change caused a performance movement.
/// </summary>
public sealed class ChangeCorrelationRule : IAuditRule
{
    /// <summary>Attributes whose changes plausibly move delivery or cost.</summary>
    private static readonly string[] MaterialAttributes =
    [
        "budget", "bid", "biddingscheme", "bidstrategy", "status", "targeting", "keyword", "negativekeyword",
    ];

    public string RuleId => "change-history-correlation";

    public string Name => "Changes coinciding with performance shifts";

    public string Description =>
        "Compares account spend and conversions in the days immediately before and after each "
        + "recorded budget, bid, status or targeting change, and reports where the two coincide.";

    public IEnumerable<Finding> Evaluate(AuditContext context)
    {
        if (context.ChangeHistoryUnavailable || context.Changes.Count == 0 || context.DailyCurrent.Count == 0)
        {
            yield break;
        }

        var settings = context.Settings;
        var formatter = context.Formatter;
        var window = settings.ChangeImpactWindowDays;
        var daily = context.DailyCurrent.ToDictionary(d => d.Date, d => d.Metrics);

        var material = context.Changes
            .Where(IsMaterial)
            .Where(c => context.Period.Current.Contains(c.ChangedOn))
            .GroupBy(c => (c.ChangedOn, c.CampaignName, c.AttributeChanged))
            .Select(g => g.First())
            .ToList();

        foreach (var change in material)
        {
            var before = Window(daily, change.ChangedOn.AddDays(-window), change.ChangedOn.AddDays(-1));
            var after = Window(daily, change.ChangedOn, change.ChangedOn.AddDays(window - 1));

            // Both sides need enough days inside the reporting period to be worth comparing.
            if (before.Days < window || after.Days < window)
            {
                continue;
            }

            var spendBefore = DailyAverage(before.Metrics, m => m.Spend);
            var spendAfter = DailyAverage(after.Metrics, m => m.Spend);
            var spend = MetricDelta.Create(spendAfter, spendBefore, MetricPolarity.Neutral);

            var conversionsBefore = DailyAverageOptional(before.Metrics, m => m.Conversions);
            var conversionsAfter = DailyAverageOptional(after.Metrics, m => m.Conversions);
            var conversions = MetricDelta.Create(conversionsAfter, conversionsBefore, MetricPolarity.HigherIsBetter);

            var spendShifted = spend.PercentChange is { } s && Math.Abs(s) >= settings.SignificantChangePercent;
            var conversionsShifted = conversions.PercentChange is { } c && Math.Abs(c) >= settings.SignificantChangePercent;

            if (!spendShifted && !conversionsShifted)
            {
                continue;
            }

            var entityLabel = change.CampaignName ?? change.EntityName ?? change.EntityType;

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{entityLabel}: {change.AttributeChanged ?? "change"} on {formatter.FormatDate(change.ChangedOn)} may be associated with a performance shift",
                Severity = FindingSeverity.Information,
                Confidence = FindingConfidence.Low,
                EntityType = change.EntityType,
                EntityName = entityLabel,
                EntityId = change.CampaignId,
                EntityContext = change.AdGroupName,
                Category = FindingCategories.Changes,
                Reason =
                    $"{change.Describe()} was recorded on {formatter.FormatDate(change.ChangedOn)}"
                    + (change.ChangedBy is null ? "" : $" by {change.ChangedBy}")
                    + $". Over the {window} day(s) before and after, average daily account spend moved from "
                    + $"{formatter.FormatCurrency(spendBefore)} to {formatter.FormatCurrency(spendAfter)} "
                    + $"({formatter.FormatChange(spend)}) and average daily conversions moved from "
                    + $"{formatter.FormatInteger(conversionsBefore)} to {formatter.FormatInteger(conversionsAfter)} "
                    + $"({formatter.FormatChange(conversions)}).",
                SuggestedAction =
                    "Review this change against the trend chart to judge whether the two are genuinely related, and "
                    + "whether the change should be kept, adjusted or reverted.",
                Caveats =
                [
                    "This is a temporal coincidence, not evidence of cause. The before-and-after figures are "
                    + "account-wide daily averages, so other campaigns, seasonality and competitor activity all "
                    + "contribute to them.",
                    "Only changes recorded by the Microsoft Advertising change history are considered. Changes made "
                    + "outside it, and changes to settings it does not expose, are not visible here.",
                ],
                Evidence =
                [
                    new Evidence("Recorded change", change.Describe()),
                    new Evidence("Changed by", change.ChangedBy ?? MetricFormatter.NotAvailable),
                    new Evidence("Avg. daily spend", formatter.FormatCurrency(spendAfter), formatter.FormatCurrency(spendBefore), formatter.FormatChange(spend)),
                    new Evidence("Avg. daily conversions", formatter.FormatInteger(conversionsAfter), formatter.FormatInteger(conversionsBefore), formatter.FormatChange(conversions)),
                ],
            };
        }
    }

    private static bool IsMaterial(ChangeRecord change)
    {
        var attribute = (change.AttributeChanged ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        var entity = (change.EntityType ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();

        return MaterialAttributes.Any(a => attribute.Contains(a, StringComparison.Ordinal) || entity.Contains(a, StringComparison.Ordinal));
    }

    private static (int Days, List<MetricSet> Metrics) Window(
        Dictionary<DateOnly, MetricSet> daily, DateOnly from, DateOnly to)
    {
        var metrics = new List<MetricSet>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (daily.TryGetValue(day, out var value))
            {
                metrics.Add(value);
            }
        }

        return (metrics.Count, metrics);
    }

    private static decimal? DailyAverage(List<MetricSet> metrics, Func<MetricSet, decimal> select)
        => metrics.Count == 0 ? null : metrics.Sum(select) / metrics.Count;

    private static decimal? DailyAverageOptional(List<MetricSet> metrics, Func<MetricSet, decimal?> select)
    {
        var total = MetricMath.SumOptional(metrics.Select(select));
        return total is null || metrics.Count == 0 ? null : total.Value / metrics.Count;
    }
}

/// <summary>
/// Reports keywords blocked by the account's own negative keywords, from the negative keyword
/// conflict report. This is a direct, high-confidence finding: the platform states the conflict.
/// </summary>
public sealed class NegativeKeywordConflictRule : IAuditRule
{
    public string RuleId => "negative-keyword-conflict";

    public string Name => "Keywords blocked by their own negative keywords";

    public string Description =>
        "Lists keywords that Microsoft Advertising reports as blocked by a negative keyword in the "
        + "same campaign or ad group.";

    public IEnumerable<Finding> Evaluate(AuditContext context)
    {
        if (context.NegativeKeywordConflicts.Count == 0)
        {
            yield break;
        }

        foreach (var group in context.NegativeKeywordConflicts.GroupBy(c => c.CampaignName ?? "Unattributed"))
        {
            var conflicts = group.ToList();

            yield return new Finding
            {
                RuleId = RuleId,
                Title = $"{group.Key}: {conflicts.Count} keyword(s) blocked by negative keywords",
                Severity = FindingSeverity.Warning,
                Confidence = FindingConfidence.High,
                EntityType = "Campaign",
                EntityName = group.Key,
                EntityId = conflicts[0].CampaignId,
                Category = FindingCategories.Relevance,
                Reason =
                    $"Microsoft Advertising reports {conflicts.Count} conflict(s) in this campaign where an active "
                    + "keyword is blocked by one of the account's own negative keywords, so the keyword cannot serve.",
                SuggestedAction =
                    "Review each conflict and decide which side is wrong: remove or narrow the negative keyword if "
                    + "the keyword should serve, or remove the keyword if the negative is correct.",
                Caveats =
                [
                    "The report identifies the conflict but not which side is intended. Some conflicts are "
                    + "deliberate, for example where traffic is routed to a different campaign.",
                ],
                Evidence =
                [
                    .. conflicts.Take(10).Select(c => new Evidence(
                        c.Keyword,
                        $"blocked by “{c.NegativeKeyword}”"
                        + (c.ConflictLevel is null ? "" : $" at {c.ConflictLevel.ToLowerInvariant()} level"))),
                ],
            };
        }
    }
}
