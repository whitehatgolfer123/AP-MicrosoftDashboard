using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Settings;

namespace MsAds.Auditor.Core.Analysis;

/// <summary>
/// Decides which way an account has moved, from its totals for two periods.
/// </summary>
/// <remarks>
/// A pure function of its inputs, so the thresholds are testable. The judgement follows the same
/// principle as the rest of the application: a rise is not assumed to be good. Efficiency (CPA)
/// is the primary signal where conversion data exists, and where it does not the verdict falls
/// back to cost and traffic and says so.
/// </remarks>
public static class HealthAssessor
{
    /// <summary>The metrics shown on a scan row, in display order.</summary>
    private static readonly MetricKey[] ScanMetrics =
    [
        MetricKey.Spend,
        MetricKey.Clicks,
        MetricKey.Conversions,
        MetricKey.CostPerAcquisition,
    ];

    public static (HealthVerdict Verdict, string Reason, bool BasedOnConversions) Assess(
        MetricSet current,
        MetricSet? previous,
        AuditSettings settings,
        MetricFormatter formatter)
    {
        var threshold = settings.SignificantChangePercent;

        // Nothing in either period: dormant, not broken.
        if (current.IsEmpty && (previous is null || previous.IsEmpty))
        {
            return (HealthVerdict.Dormant,
                "No delivery in either period. The account appears to be dormant.", false);
        }

        // Delivered before, nothing now: the state most worth acting on.
        if (current.IsEmpty && previous is { IsEmpty: false })
        {
            return (HealthVerdict.NotDelivering,
                $"Spent {formatter.FormatCurrency(previous.Spend)} in the previous period and nothing in "
                + "this one. Check budgets, campaign status and ad eligibility.", false);
        }

        if (previous is null)
        {
            return (HealthVerdict.Unknown,
                $"Spent {formatter.FormatCurrency(current.Spend)}, but no comparison period was retrieved, "
                + "so no direction can be given.", current.HasConversionData);
        }

        var spend = MetricDelta.Create(current.Spend, previous.Spend, MetricPolarity.Neutral);

        // With conversion data, efficiency is the signal that matters.
        if (current.HasConversionData && previous.HasConversionData)
        {
            var cpa = MetricDelta.Create(current.CostPerAcquisition, previous.CostPerAcquisition, MetricPolarity.LowerIsBetter);
            var conversions = MetricDelta.Create(current.Conversions, previous.Conversions, MetricPolarity.HigherIsBetter);

            // Spending with nothing to show for it outranks any percentage movement.
            if (current.HasSpendButNoConversions)
            {
                return (HealthVerdict.Declining,
                    $"Spent {formatter.FormatCurrency(current.Spend)} with no recorded conversions this period.",
                    true);
            }

            if (cpa.PercentChange is { } cpaChange)
            {
                if (cpaChange >= threshold)
                {
                    return (HealthVerdict.Declining,
                        $"CPA rose {formatter.FormatChange(cpa)} to {formatter.FormatCurrency(cpa.Current)}, "
                        + $"on spend {formatter.FormatChange(spend)} and conversions {formatter.FormatChange(conversions)}.",
                        true);
                }

                if (cpaChange <= -threshold)
                {
                    return (HealthVerdict.Improving,
                        $"CPA fell {formatter.FormatChange(cpa)} to {formatter.FormatCurrency(cpa.Current)}, "
                        + $"on spend {formatter.FormatChange(spend)} and conversions {formatter.FormatChange(conversions)}.",
                        true);
                }
            }

            // CPA held, but conversions can still have moved enough to matter.
            if (conversions.Kind is ChangeKind.CeasedActivity)
            {
                return (HealthVerdict.Declining,
                    $"Conversions stopped this period, on {formatter.FormatCurrency(current.Spend)} of spend.", true);
            }

            return (HealthVerdict.Stable,
                $"CPA moved {formatter.FormatChange(cpa)} and conversions {formatter.FormatChange(conversions)}, "
                + $"both within the {threshold:N0}% threshold for a significant change.",
                true);
        }

        // No conversion data: judge cost of traffic, and be explicit that this is weaker.
        var cpc = MetricDelta.Create(current.AverageCpc, previous.AverageCpc, MetricPolarity.LowerIsBetter);
        var clicks = MetricDelta.Create(current.Clicks, previous.Clicks, MetricPolarity.Neutral);

        if (cpc.PercentChange is { } cpcChange && Math.Abs(cpcChange) >= threshold)
        {
            var declining = cpcChange > 0;

            return (declining ? HealthVerdict.Declining : HealthVerdict.Improving,
                $"No conversion data, so this is based on cost of traffic only: CPC "
                + $"{formatter.FormatChange(cpc)} to {formatter.FormatCurrency(cpc.Current)}, clicks "
                + $"{formatter.FormatChange(clicks)}.",
                false);
        }

        return (HealthVerdict.Stable,
            $"No conversion data, so this is based on cost of traffic only: CPC moved "
            + $"{formatter.FormatChange(cpc)} and spend {formatter.FormatChange(spend)}.",
            false);
    }

    /// <summary>The comparisons shown on a scan row.</summary>
    public static IReadOnlyList<MetricComparison> KeyMetrics(MetricSet current, MetricSet? previous)
        => [.. ScanMetrics.Select(key => new MetricComparison(
            key,
            MetricDelta.Create(current.Get(key), previous?.Get(key), MetricCatalogue.Describe(key).Polarity)))];
}
