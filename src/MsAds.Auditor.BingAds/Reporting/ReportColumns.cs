using Microsoft.BingAds.V13.Reporting;

namespace MsAds.Auditor.BingAds.Reporting;

/// <summary>
/// The columns requested for each report.
/// </summary>
/// <remarks>
/// Kept deliberately small: every extra column slows the report down, and the application only
/// calculates from what it displays. Both the whole-number <c>Conversions</c> and the fractional
/// <c>ConversionsQualified</c> columns are requested, because accounts differ in which they
/// populate and reading the wrong one would silently lose fractional conversions.
/// </remarks>
internal static class ReportColumns
{
    public static IList<AccountPerformanceReportColumn> AccountDaily =>
    [
        AccountPerformanceReportColumn.TimePeriod,
        AccountPerformanceReportColumn.AccountId,
        AccountPerformanceReportColumn.CurrencyCode,
        AccountPerformanceReportColumn.Impressions,
        AccountPerformanceReportColumn.Clicks,
        AccountPerformanceReportColumn.Spend,
        AccountPerformanceReportColumn.Conversions,
        AccountPerformanceReportColumn.ConversionsQualified,
        AccountPerformanceReportColumn.Revenue,
    ];

    public static IList<CampaignPerformanceReportColumn> Campaign =>
    [
        CampaignPerformanceReportColumn.CampaignId,
        CampaignPerformanceReportColumn.CampaignName,
        CampaignPerformanceReportColumn.CampaignStatus,
        CampaignPerformanceReportColumn.CampaignType,
        CampaignPerformanceReportColumn.Impressions,
        CampaignPerformanceReportColumn.Clicks,
        CampaignPerformanceReportColumn.Spend,
        CampaignPerformanceReportColumn.Conversions,
        CampaignPerformanceReportColumn.ConversionsQualified,
        CampaignPerformanceReportColumn.Revenue,
        CampaignPerformanceReportColumn.ImpressionSharePercent,
        CampaignPerformanceReportColumn.ImpressionLostToBudgetPercent,
        CampaignPerformanceReportColumn.ImpressionLostToRankAggPercent,
    ];

    public static IList<SearchQueryPerformanceReportColumn> SearchQuery =>
    [
        SearchQueryPerformanceReportColumn.SearchQuery,
        SearchQueryPerformanceReportColumn.CampaignId,
        SearchQueryPerformanceReportColumn.CampaignName,
        SearchQueryPerformanceReportColumn.AdGroupId,
        SearchQueryPerformanceReportColumn.AdGroupName,
        SearchQueryPerformanceReportColumn.Keyword,
        SearchQueryPerformanceReportColumn.BidMatchType,
        SearchQueryPerformanceReportColumn.DeliveredMatchType,
        SearchQueryPerformanceReportColumn.Impressions,
        SearchQueryPerformanceReportColumn.Clicks,
        SearchQueryPerformanceReportColumn.Spend,
        SearchQueryPerformanceReportColumn.Conversions,
        SearchQueryPerformanceReportColumn.ConversionsQualified,
        SearchQueryPerformanceReportColumn.Revenue,
    ];

    public static IList<KeywordPerformanceReportColumn> Keyword =>
    [
        KeywordPerformanceReportColumn.KeywordId,
        KeywordPerformanceReportColumn.Keyword,
        KeywordPerformanceReportColumn.KeywordStatus,
        KeywordPerformanceReportColumn.BidMatchType,
        KeywordPerformanceReportColumn.CampaignId,
        KeywordPerformanceReportColumn.CampaignName,
        KeywordPerformanceReportColumn.AdGroupId,
        KeywordPerformanceReportColumn.AdGroupName,
        KeywordPerformanceReportColumn.QualityScore,
        KeywordPerformanceReportColumn.CurrentMaxCpc,
        KeywordPerformanceReportColumn.Impressions,
        KeywordPerformanceReportColumn.Clicks,
        KeywordPerformanceReportColumn.Spend,
        KeywordPerformanceReportColumn.Conversions,
        KeywordPerformanceReportColumn.ConversionsQualified,
        KeywordPerformanceReportColumn.Revenue,
    ];

    public static IList<SearchCampaignChangeHistoryReportColumn> ChangeHistory =>
    [
        SearchCampaignChangeHistoryReportColumn.DateTime,
        SearchCampaignChangeHistoryReportColumn.ChangedBy,
        SearchCampaignChangeHistoryReportColumn.CampaignId,
        SearchCampaignChangeHistoryReportColumn.CampaignName,
        SearchCampaignChangeHistoryReportColumn.AdGroupName,
        SearchCampaignChangeHistoryReportColumn.ItemChanged,
        SearchCampaignChangeHistoryReportColumn.AttributeChanged,
        SearchCampaignChangeHistoryReportColumn.HowChanged,
        SearchCampaignChangeHistoryReportColumn.OldValue,
        SearchCampaignChangeHistoryReportColumn.NewValue,
        SearchCampaignChangeHistoryReportColumn.EntityName,
        SearchCampaignChangeHistoryReportColumn.EntityId,
    ];

    public static IList<NegativeKeywordConflictReportColumn> NegativeKeywordConflict =>
    [
        NegativeKeywordConflictReportColumn.CampaignId,
        NegativeKeywordConflictReportColumn.CampaignName,
        NegativeKeywordConflictReportColumn.AdGroupName,
        NegativeKeywordConflictReportColumn.Keyword,
        NegativeKeywordConflictReportColumn.NegativeKeyword,
        NegativeKeywordConflictReportColumn.ConflictLevel,
        NegativeKeywordConflictReportColumn.NegativeKeywordList,
    ];
}
