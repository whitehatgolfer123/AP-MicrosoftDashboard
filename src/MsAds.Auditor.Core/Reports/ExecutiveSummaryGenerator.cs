using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Rules;

namespace MsAds.Auditor.Core.Reports;

/// <summary>
/// Builds the executive summary deterministically from the report's own figures.
/// </summary>
public sealed class ExecutiveSummaryGenerator
{
    public ExecutiveSummary Generate(
        AuditContext context,
        RecommendationResult recommendations,
        SearchTermAnalysis searchTerms)
    {
        var formatter = context.Formatter;
        var current = context.AccountCurrent;
        var previous = context.AccountPrevious;
        var points = new List<string>();
        var caveats = new List<string>();

        var headline = BuildHeadline(context, formatter);

        // Where the spend moved most, named so the reader knows where to look first.
        var biggestMover = context.Campaigns
            .Where(c => c.HasComparison)
            .Select(c => (Campaign: c, Change: c.Current.Spend - c.Previous!.Spend))
            .Where(x => Math.Abs(x.Change) > 0m)
            .OrderByDescending(x => Math.Abs(x.Change))
            .FirstOrDefault();

        if (biggestMover.Campaign is not null)
        {
            var direction = biggestMover.Change > 0m ? "increase" : "reduction";
            points.Add(
                $"The largest single contributor to the change in spend was {biggestMover.Campaign.Name}, "
                + $"a {formatter.FormatCurrency(Math.Abs(biggestMover.Change))} {direction} "
                + $"({formatter.FormatChange(biggestMover.Campaign.Delta(MetricKey.Spend))}).");
        }

        // Campaigns taking money without returning conversions.
        if (!context.ConversionDataUnavailable)
        {
            var noConversionCampaigns = context.Campaigns
                .Where(c => c.Current.HasSpendButNoConversions && c.Current.Spend >= context.Settings.MinimumSpend)
                .OrderByDescending(c => c.Current.Spend)
                .ToList();

            if (noConversionCampaigns.Count > 0)
            {
                var total = noConversionCampaigns.Sum(c => c.Current.Spend);
                points.Add(
                    $"{noConversionCampaigns.Count} campaign(s) spent {formatter.FormatCurrency(total)} without "
                    + $"recording a conversion, the largest being {noConversionCampaigns[0].Name} at "
                    + $"{formatter.FormatCurrency(noConversionCampaigns[0].Current.Spend)}.");
            }
        }

        // Search term exposure, stated as a subset of total spend rather than as a total.
        if (searchTerms.DataUnavailable)
        {
            caveats.Add(
                "Search term data could not be retrieved for this period, so no conclusion about wasted search "
                + "spend can be drawn either way.");
        }
        else if (searchTerms.TermCount > 0)
        {
            if (searchTerms.UnproductiveSpend > 0m)
            {
                points.Add(
                    $"Within the search terms that Microsoft Advertising reported, "
                    + $"{formatter.FormatCurrency(searchTerms.UnproductiveSpend)} was spent on queries with no "
                    + $"recorded conversions, of {formatter.FormatCurrency(searchTerms.SpendAnalysed)} covered by "
                    + $"the report. {searchTerms.SuggestedNegativeKeywords.Count} term(s) meet the evidence bar to "
                    + "be proposed as negative keywords.");
            }
            else
            {
                points.Add(
                    $"No search term in the {searchTerms.TermCount} reported met the waste thresholds for this period.");
            }

            caveats.Add(
                "Microsoft Advertising does not publish every search query, so search term figures cover a subset "
                + "of total spend and cannot be used to rule out waste.");
        }

        // What the rules found, so the reader knows how much is waiting for them.
        var critical = recommendations.CountOf(FindingSeverity.Critical);
        var warnings = recommendations.CountOf(FindingSeverity.Warning);
        var positives = recommendations.CountOf(FindingSeverity.Positive);

        points.Add(
            $"The rules engine produced {recommendations.Findings.Count} finding(s): {critical} needing attention "
            + $"now, {warnings} to investigate, and {positives} where performance looks strong.");

        if (!context.Settings.HasTargetCpa && !context.Settings.HasTargetRoas)
        {
            caveats.Add(
                "No target CPA or ROAS is configured for this account, so performance is described relative to the "
                + "account's own averages rather than judged against a commercial objective. Setting targets in "
                + "Settings will enable the target-based rules.");
        }

        if (context.ConversionDataUnavailable)
        {
            caveats.Add(
                "Conversion data was unavailable, so this summary covers cost and traffic only. Conversion-based "
                + "rules were suppressed rather than treated as zero.");
        }

        if (previous is null)
        {
            caveats.Add("No comparison period was retrieved, so no period-on-period statement can be made.");
        }

        if (context.Period.CurrentDayExcluded)
        {
            caveats.Add(
                $"Today ({formatter.FormatDate(context.Period.AccountToday)} in the account's "
                + $"{context.Period.TimeZoneId} time zone) is excluded because it is still accumulating data.");
        }

        if (context.ChangeHistoryUnavailable)
        {
            caveats.Add("Change history could not be retrieved, so no change could be related to performance.");
        }

        return new ExecutiveSummary { Headline = headline, Points = points, Caveats = caveats };
    }

    /// <summary>
    /// The headline sentence. States the spend and conversion movement and the CPA consequence,
    /// without assuming that a rise in either is good or bad.
    /// </summary>
    private static string BuildHeadline(AuditContext context, MetricFormatter formatter)
    {
        var current = context.AccountCurrent;
        var previous = context.AccountPrevious;

        if (current.IsEmpty)
        {
            return "No delivery was recorded in this period, so there is nothing to compare.";
        }

        if (previous is null)
        {
            return $"The account spent {formatter.FormatCurrency(current.Spend)} across "
                + $"{formatter.FormatInteger(current.Clicks)} clicks and "
                + $"{formatter.FormatInteger(current.Impressions)} impressions, recording "
                + $"{formatter.FormatInteger(current.Conversions)} conversion(s) at a CPA of "
                + $"{formatter.FormatCurrency(current.CostPerAcquisition)}.";
        }

        var spend = MetricDelta.Create(current.Spend, previous.Spend, MetricPolarity.Neutral);
        var conversions = MetricDelta.Create(current.Conversions, previous.Conversions, MetricPolarity.HigherIsBetter);
        var cpa = MetricDelta.Create(current.CostPerAcquisition, previous.CostPerAcquisition, MetricPolarity.LowerIsBetter);

        var spendPhrase = Describe(spend, "Spend", formatter, context.Settings.SignificantChangePercent);
        var conversionPhrase = context.ConversionDataUnavailable
            ? "Conversion data was unavailable for comparison"
            : Describe(conversions, "conversions", formatter, context.Settings.SignificantChangePercent);

        var cpaPhrase = cpa.Kind switch
        {
            ChangeKind.NotComparable => "CPA could not be compared.",
            ChangeKind.NoChange => "CPA was unchanged.",
            _ => $"CPA therefore {(cpa.Verdict is Verdict.Negative ? "increased" : "decreased")} "
                 + $"{formatter.FormatChange(cpa)}, to {formatter.FormatCurrency(cpa.Current)}.",
        };

        return $"{spendPhrase}, while {conversionPhrase}. {cpaPhrase}";
    }

    private static string Describe(
        MetricDelta delta, string subject, MetricFormatter formatter, decimal significantPercent)
    {
        var magnitude = delta.PercentChange is { } p && Math.Abs(p) < significantPercent ? "slightly " : "";

        return delta.Kind switch
        {
            ChangeKind.NotComparable => $"{subject} could not be compared",
            ChangeKind.NoChange => $"{subject} was unchanged",
            ChangeKind.NewActivity => $"{subject} began in this period",
            ChangeKind.CeasedActivity => $"{subject} fell to zero",
            ChangeKind.Increase => $"{subject} {magnitude}increased by {formatter.FormatChange(delta)} to "
                                   + $"{Value(delta.Current, subject, formatter)}",
            _ => $"{subject} {magnitude}decreased by {formatter.FormatChange(delta)} to "
                 + $"{Value(delta.Current, subject, formatter)}",
        };
    }

    private static string Value(decimal? value, string subject, MetricFormatter formatter)
        => subject.Equals("Spend", StringComparison.OrdinalIgnoreCase)
            ? formatter.FormatCurrency(value)
            : formatter.FormatInteger(value);
}
