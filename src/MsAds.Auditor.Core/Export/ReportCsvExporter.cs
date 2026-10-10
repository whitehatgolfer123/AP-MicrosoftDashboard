using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Rules;

namespace MsAds.Auditor.Core.Export;

/// <summary>
/// Turns report sections into CSV. Numbers are exported unformatted so they can be used directly
/// in a spreadsheet, with the currency named in the preamble instead of repeated per cell.
/// </summary>
public static class ReportCsvExporter
{
    /// <summary>Available exports, used to build the download links.</summary>
    public static IReadOnlyList<(string Key, string Label)> Available { get; } =
    [
        ("campaigns", "Campaign performance"),
        ("search-terms", "Search term opportunities"),
        ("keywords", "Keyword performance"),
        ("recommendations", "Recommendations"),
        ("negative-keywords", "Proposed negative keywords"),
        ("changes", "Change history"),
        ("daily", "Daily performance"),
    ];

    public static string Export(AuditReport report, string key) => key switch
    {
        "campaigns" => Campaigns(report),
        "search-terms" => SearchTerms(report),
        "keywords" => Keywords(report),
        "recommendations" => Recommendations(report),
        "negative-keywords" => NegativeKeywords(report),
        "changes" => Changes(report),
        "daily" => Daily(report),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown export."),
    };

    public static string FileName(AuditReport report, string key)
        => $"{report.Account.AccountId}-{key}-{report.Period.Current.Start:yyyyMMdd}-{report.Period.Current.End:yyyyMMdd}"
           + (report.IsDemo ? "-DEMO" : "") + ".csv";

    /// <summary>
    /// The provenance block written at the top of every export: which account, which dates, and
    /// whether the figures are live or demo. Without it an exported file is unattributable.
    /// </summary>
    private static CsvWriter WithPreamble(AuditReport report, string section)
    {
        var writer = new CsvWriter();

        writer.Preamble($"Microsoft Ads Performance Auditor — {section}");
        writer.Preamble($"Account: {report.Account.AccountName} ({report.Account.AccountId})");
        writer.Preamble($"Reporting period: {CsvWriter.Date(report.Period.Current.Start)} to {CsvWriter.Date(report.Period.Current.End)} ({report.Period.Current.Days} days)");
        writer.Preamble(report.Period.Previous is { } previous
            ? $"Comparison period: {CsvWriter.Date(previous.Start)} to {CsvWriter.Date(previous.End)}"
            : "Comparison period: none");
        writer.Preamble($"Reporting time zone: {report.Period.TimeZoneId}");
        writer.Preamble($"Currency: {report.Formatter.CurrencyCode} (amounts are unformatted numbers)");
        writer.Preamble($"Generated: {CsvWriter.DateTime(report.GeneratedAt)} UTC");
        writer.Preamble($"Data source: {report.SourceName}");

        if (report.IsDemo)
        {
            writer.Preamble("WARNING: DEMO DATA — these figures are synthetic and are not live advertising performance.");
        }

        if (report.ConversionDataUnavailable)
        {
            writer.Preamble("WARNING: conversion data was unavailable; conversion columns are empty rather than zero.");
        }

        writer.Blank();
        return writer;
    }

    public static string Campaigns(AuditReport report)
    {
        var writer = WithPreamble(report, "Campaign performance");

        writer.Header(
            "Campaign ID", "Campaign", "Status", "Impressions", "Clicks", "CTR %", "Spend", "Avg CPC",
            "Conversions", "Conv rate %", "CPA", "Conv value", "ROAS",
            "Prev impressions", "Prev clicks", "Prev spend", "Prev conversions", "Prev CPA",
            "Spend change %", "Conversions change %", "CPA change %",
            "Impression share %", "IS lost to budget %", "IS lost to rank %");

        foreach (var campaign in report.Campaigns)
        {
            var c = campaign.Current;
            var p = campaign.Previous;

            writer.Row(
                CsvWriter.Number(campaign.EntityId),
                campaign.Name,
                campaign.Status,
                CsvWriter.Number(c.Impressions),
                CsvWriter.Number(c.Clicks),
                CsvWriter.Number(c.ClickThroughRate),
                CsvWriter.Number(c.Spend),
                CsvWriter.Number(c.AverageCpc),
                CsvWriter.Number(c.Conversions),
                CsvWriter.Number(c.ConversionRate),
                CsvWriter.Number(c.CostPerAcquisition),
                CsvWriter.Number(c.ConversionValue),
                CsvWriter.Number(c.ReturnOnAdSpend),
                p is null ? "" : CsvWriter.Number(p.Impressions),
                p is null ? "" : CsvWriter.Number(p.Clicks),
                CsvWriter.Number(p?.Spend),
                CsvWriter.Number(p?.Conversions),
                CsvWriter.Number(p?.CostPerAcquisition),
                CsvWriter.Number(campaign.Delta(MetricKey.Spend).PercentChange),
                CsvWriter.Number(campaign.Delta(MetricKey.Conversions).PercentChange),
                CsvWriter.Number(campaign.Delta(MetricKey.CostPerAcquisition).PercentChange),
                CsvWriter.Number(campaign.ImpressionShare),
                CsvWriter.Number(campaign.ImpressionShareLostToBudget),
                CsvWriter.Number(campaign.ImpressionShareLostToRank));
        }

        return writer.Build();
    }

    public static string SearchTerms(AuditReport report)
    {
        var writer = WithPreamble(report, "Search term opportunities");

        writer.Preamble(
            "Microsoft Advertising does not report every search query. Spend below covers only the queries it "
            + "published, so it is a subset of total spend and cannot be used to rule out waste.");
        writer.Blank();

        writer.Header(
            "Search term", "Campaign", "Ad group", "Matched keyword", "Bid match type", "Delivered match type",
            "Impressions", "Clicks", "CTR %", "Spend", "Avg CPC", "Conversions", "Conv rate %", "CPA",
            "Flags", "Reason", "Recommended action", "Confidence", "Negative keyword candidate", "Caveats");

        foreach (var assessment in report.SearchTerms.Assessments)
        {
            var term = assessment.Term;
            var m = term.Metrics;

            writer.Row(
                term.SearchTerm,
                term.CampaignName,
                term.AdGroupName,
                term.Keyword,
                term.BidMatchType,
                term.DeliveredMatchType,
                CsvWriter.Number(m.Impressions),
                CsvWriter.Number(m.Clicks),
                CsvWriter.Number(m.ClickThroughRate),
                CsvWriter.Number(m.Spend),
                CsvWriter.Number(m.AverageCpc),
                CsvWriter.Number(m.Conversions),
                CsvWriter.Number(m.ConversionRate),
                CsvWriter.Number(m.CostPerAcquisition),
                assessment.FlagSummary,
                assessment.Reason,
                assessment.RecommendedAction.Label(),
                assessment.Confidence.ToString(),
                assessment.IsNegativeKeywordCandidate ? "Yes" : "No",
                string.Join(" ", assessment.Caveats));
        }

        return writer.Build();
    }

    public static string Keywords(AuditReport report)
    {
        var writer = WithPreamble(report, "Keyword performance");

        writer.Header(
            "Keyword ID", "Keyword", "Campaign / ad group", "Status", "Quality score",
            "Impressions", "Clicks", "CTR %", "Spend", "Avg CPC", "Conversions", "Conv rate %", "CPA",
            "Prev spend", "Prev conversions", "Prev CPA", "Spend change %", "CPA change %");

        foreach (var keyword in report.Keywords)
        {
            var c = keyword.Current;
            var p = keyword.Previous;

            writer.Row(
                CsvWriter.Number(keyword.EntityId),
                keyword.Name,
                keyword.Context,
                keyword.Status,
                CsvWriter.Number(keyword.QualityScore, 1),
                CsvWriter.Number(c.Impressions),
                CsvWriter.Number(c.Clicks),
                CsvWriter.Number(c.ClickThroughRate),
                CsvWriter.Number(c.Spend),
                CsvWriter.Number(c.AverageCpc),
                CsvWriter.Number(c.Conversions),
                CsvWriter.Number(c.ConversionRate),
                CsvWriter.Number(c.CostPerAcquisition),
                CsvWriter.Number(p?.Spend),
                CsvWriter.Number(p?.Conversions),
                CsvWriter.Number(p?.CostPerAcquisition),
                CsvWriter.Number(keyword.Delta(MetricKey.Spend).PercentChange),
                CsvWriter.Number(keyword.Delta(MetricKey.CostPerAcquisition).PercentChange));
        }

        return writer.Build();
    }

    public static string Recommendations(AuditReport report)
    {
        var writer = WithPreamble(report, "Recommendations");

        writer.Header(
            "Severity", "Confidence", "Category", "Entity type", "Entity", "Entity ID", "Finding",
            "Reason", "Suggested action", "Supporting data", "Caveats", "Rule ID");

        foreach (var finding in report.Recommendations.Findings)
        {
            writer.Row(
                finding.Severity.ToString(),
                finding.Confidence.ToString(),
                finding.Category,
                finding.EntityType,
                finding.EntityName,
                finding.EntityId is null ? "" : CsvWriter.Number(finding.EntityId.Value),
                finding.Title,
                finding.Reason,
                finding.SuggestedAction,
                string.Join("; ", finding.Evidence.Select(FormatEvidence)),
                string.Join(" ", finding.Caveats),
                finding.RuleId);
        }

        return writer.Build();
    }

    private static string FormatEvidence(Evidence evidence)
    {
        var text = $"{evidence.Label}: {evidence.Value}";

        if (evidence.Previous is not null)
        {
            text += $" (was {evidence.Previous}";
            text += evidence.Change is null ? ")" : $", {evidence.Change})";
        }

        return text;
    }

    public static string NegativeKeywords(AuditReport report)
    {
        var writer = WithPreamble(report, "Proposed negative keywords");

        writer.Preamble(
            "These are proposals for review. Nothing is applied automatically: a change is only made when "
            + "someone selects it and confirms it. Adding a negative keyword blocks all future matching of "
            + "the query, so review the wording first.");
        writer.Blank();

        writer.Header(
            "Search term", "Campaign", "Ad group", "Clicks", "Spend", "Conversions", "Reason", "Confidence");

        foreach (var candidate in report.SearchTerms.NegativeKeywordCandidates
            .OrderByDescending(a => a.Term.Metrics.Spend))
        {
            writer.Row(
                candidate.Term.SearchTerm,
                candidate.Term.CampaignName,
                candidate.Term.AdGroupName,
                CsvWriter.Number(candidate.Term.Metrics.Clicks),
                CsvWriter.Number(candidate.Term.Metrics.Spend),
                CsvWriter.Number(candidate.Term.Metrics.Conversions),
                candidate.Reason,
                candidate.Confidence.ToString());
        }

        return writer.Build();
    }

    public static string Changes(AuditReport report)
    {
        var writer = WithPreamble(report, "Change history");

        if (report.ChangeHistoryUnavailable)
        {
            writer.Preamble("Change history could not be retrieved for this period.");
            writer.Blank();
        }

        writer.Header(
            "Changed at", "Changed by", "Entity type", "Entity", "Campaign", "Ad group",
            "Attribute", "How changed", "Old value", "New value");

        foreach (var change in report.Changes)
        {
            writer.Row(
                CsvWriter.DateTime(change.ChangedAt),
                change.ChangedBy,
                change.EntityType,
                change.EntityName,
                change.CampaignName,
                change.AdGroupName,
                change.AttributeChanged,
                change.HowChanged,
                change.OldValue,
                change.NewValue);
        }

        return writer.Build();
    }

    public static string Daily(AuditReport report)
    {
        var writer = WithPreamble(report, "Daily performance");

        writer.Header(
            "Date", "Period", "Impressions", "Clicks", "CTR %", "Spend", "Avg CPC",
            "Conversions", "Conv rate %", "CPA", "Conv value", "ROAS");

        foreach (var (day, label) in report.DailyPrevious.Select(d => (d, "Previous"))
            .Concat(report.DailyCurrent.Select(d => (d, "Current"))))
        {
            var m = day.Metrics;

            writer.Row(
                CsvWriter.Date(day.Date),
                label,
                CsvWriter.Number(m.Impressions),
                CsvWriter.Number(m.Clicks),
                CsvWriter.Number(m.ClickThroughRate),
                CsvWriter.Number(m.Spend),
                CsvWriter.Number(m.AverageCpc),
                CsvWriter.Number(m.Conversions),
                CsvWriter.Number(m.ConversionRate),
                CsvWriter.Number(m.CostPerAcquisition),
                CsvWriter.Number(m.ConversionValue),
                CsvWriter.Number(m.ReturnOnAdSpend));
        }

        return writer.Build();
    }
}
