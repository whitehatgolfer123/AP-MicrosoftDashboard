using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;

namespace MsAds.Auditor.BingAds.Reporting;

/// <summary>
/// Maps report rows onto the domain models. Pure functions over <see cref="IReportRow"/>, so the
/// whole mapping is unit tested against fabricated rows with no API access.
/// </summary>
public static class ReportRowMapper
{
    /// <summary>The performance counters shared by every performance report.</summary>
    public static MetricSet Metrics(IReportRow row) => new()
    {
        Impressions = ReportValue.Integer(row, "Impressions"),
        Clicks = ReportValue.Integer(row, "Clicks"),
        Spend = ReportValue.Money(row, "Spend"),
        Conversions = ReportValue.Conversions(row),
        ConversionValue = ReportValue.Number(row, "Revenue"),
    };

    /// <summary>One day of account totals, or null when the row carries no usable date.</summary>
    public static DailyMetrics? Daily(IReportRow row)
        => ReportValue.Date(row, "TimePeriod") is { } date ? new DailyMetrics(date, Metrics(row)) : null;

    public static CampaignPerformance Campaign(IReportRow row) => new()
    {
        CampaignId = ReportValue.Integer(row, "CampaignId"),
        CampaignName = ReportValue.Text(row, "CampaignName") ?? "(unnamed campaign)",
        Status = ReportValue.Text(row, "CampaignStatus"),
        CampaignType = ReportValue.Text(row, "CampaignType"),
        Metrics = Metrics(row),
        ImpressionShare = ReportValue.Number(row, "ImpressionSharePercent"),
        ImpressionShareLostToBudget = ReportValue.Number(row, "ImpressionLostToBudgetPercent"),
        ImpressionShareLostToRank = ReportValue.Number(row, "ImpressionLostToRankAggPercent"),
    };

    public static SearchTermPerformance? SearchTerm(IReportRow row)
    {
        var term = ReportValue.Text(row, "SearchQuery");

        // A row with no query is not usable; it would appear in the table as a blank term.
        return term is null
            ? null
            : new SearchTermPerformance
            {
                SearchTerm = term,
                CampaignId = ReportValue.Number(row, "CampaignId") is { } id ? (long)id : null,
                CampaignName = ReportValue.Text(row, "CampaignName"),
                AdGroupId = ReportValue.Number(row, "AdGroupId") is { } adGroupId ? (long)adGroupId : null,
                AdGroupName = ReportValue.Text(row, "AdGroupName"),
                Keyword = ReportValue.Text(row, "Keyword"),
                BidMatchType = ReportValue.Text(row, "BidMatchType"),
                DeliveredMatchType = ReportValue.Text(row, "DeliveredMatchType"),
                Metrics = Metrics(row),
            };
    }

    public static KeywordPerformance Keyword(IReportRow row) => new()
    {
        KeywordId = ReportValue.Integer(row, "KeywordId"),
        Keyword = ReportValue.Text(row, "Keyword") ?? "(unnamed keyword)",
        MatchType = ReportValue.Text(row, "BidMatchType"),
        CampaignId = ReportValue.Number(row, "CampaignId") is { } id ? (long)id : null,
        CampaignName = ReportValue.Text(row, "CampaignName"),
        AdGroupId = ReportValue.Number(row, "AdGroupId") is { } adGroupId ? (long)adGroupId : null,
        AdGroupName = ReportValue.Text(row, "AdGroupName"),
        Status = ReportValue.Text(row, "KeywordStatus"),
        QualityScore = ReportValue.Number(row, "QualityScore"),
        CurrentBid = ReportValue.Number(row, "CurrentMaxCpc"),
        Metrics = Metrics(row),
    };

    public static ChangeRecord? Change(IReportRow row)
    {
        // Without a timestamp a change cannot be placed against performance, so it is dropped
        // rather than shown at an invented time.
        if (ReportValue.Timestamp(row, "DateTime") is not { } changedAt)
        {
            return null;
        }

        return new ChangeRecord
        {
            ChangedAt = changedAt,
            ChangedBy = ReportValue.Text(row, "ChangedBy"),
            EntityType = ReportValue.Text(row, "ItemChanged") ?? "Entity",
            EntityName = ReportValue.Text(row, "EntityName"),
            CampaignId = ReportValue.Number(row, "CampaignId") is { } id ? (long)id : null,
            CampaignName = ReportValue.Text(row, "CampaignName"),
            AdGroupName = ReportValue.Text(row, "AdGroupName"),
            AttributeChanged = ReportValue.Text(row, "AttributeChanged"),
            HowChanged = ReportValue.Text(row, "HowChanged"),
            OldValue = ReportValue.Text(row, "OldValue"),
            NewValue = ReportValue.Text(row, "NewValue"),
        };
    }

    public static NegativeKeywordConflict? Conflict(IReportRow row)
    {
        var keyword = ReportValue.Text(row, "Keyword");
        var negative = ReportValue.Text(row, "NegativeKeyword");

        return keyword is null || negative is null
            ? null
            : new NegativeKeywordConflict
            {
                CampaignId = ReportValue.Number(row, "CampaignId") is { } id ? (long)id : null,
                CampaignName = ReportValue.Text(row, "CampaignName"),
                AdGroupName = ReportValue.Text(row, "AdGroupName"),
                Keyword = keyword,
                NegativeKeyword = negative,
                ConflictLevel = ReportValue.Text(row, "ConflictLevel"),
                NegativeKeywordListName = ReportValue.Text(row, "NegativeKeywordList"),
            };
    }
}
