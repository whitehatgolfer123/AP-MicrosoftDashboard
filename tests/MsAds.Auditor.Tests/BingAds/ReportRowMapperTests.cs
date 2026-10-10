using MsAds.Auditor.BingAds.Reporting;

namespace MsAds.Auditor.Tests.BingAds;

/// <summary>
/// Mapping of report rows onto the domain models, exercised against fabricated rows so that the
/// whole live-data path above the API is covered without credentials.
/// </summary>
public class ReportRowMapperTests
{
    private sealed class FakeRow(params (string Column, string? Value)[] cells) : IReportRow
    {
        private readonly Dictionary<string, string?> _cells =
            cells.ToDictionary(c => c.Column, c => c.Value, StringComparer.OrdinalIgnoreCase);

        public string? Raw(string column) => _cells.GetValueOrDefault(column);
    }

    [Fact]
    public void Metrics_map_from_the_performance_columns()
    {
        var metrics = ReportRowMapper.Metrics(new FakeRow(
            ("Impressions", "10000"), ("Clicks", "400"), ("Spend", "800.00"),
            ("ConversionsQualified", "20"), ("Revenue", "4000")));

        Assert.Equal(10_000, metrics.Impressions);
        Assert.Equal(400, metrics.Clicks);
        Assert.Equal(800m, metrics.Spend);
        Assert.Equal(20m, metrics.Conversions);
        Assert.Equal(4000m, metrics.ConversionValue);

        // Derived figures come from the shared calculation layer.
        Assert.Equal(4m, metrics.ClickThroughRate);
        Assert.Equal(40m, metrics.CostPerAcquisition);
        Assert.Equal(5m, metrics.ReturnOnAdSpend);
    }

    [Fact]
    public void A_row_without_conversion_columns_reports_no_conversion_data()
    {
        var metrics = ReportRowMapper.Metrics(new FakeRow(
            ("Impressions", "100"), ("Clicks", "10"), ("Spend", "20")));

        Assert.False(metrics.HasConversionData);
        Assert.Null(metrics.CostPerAcquisition);
    }

    [Fact]
    public void A_campaign_row_maps_its_identity_status_and_impression_share()
    {
        var campaign = ReportRowMapper.Campaign(new FakeRow(
            ("CampaignId", "1234"), ("CampaignName", "Brand – Exact"), ("CampaignStatus", "Active"),
            ("CampaignType", "Search"), ("Impressions", "500"), ("Clicks", "50"), ("Spend", "75"),
            ("ImpressionSharePercent", "72.4"), ("ImpressionLostToBudgetPercent", "24.8"),
            ("ImpressionLostToRankAggPercent", "2.8")));

        Assert.Equal(1234, campaign.CampaignId);
        Assert.Equal("Brand – Exact", campaign.CampaignName);
        Assert.Equal("Active", campaign.Status);
        Assert.Equal(72.4m, campaign.ImpressionShare);
        Assert.Equal(24.8m, campaign.ImpressionShareLostToBudget);
        Assert.True(campaign.HasImpressionShareData);
    }

    [Fact]
    public void A_campaign_row_without_impression_share_reports_none_rather_than_zero()
    {
        var campaign = ReportRowMapper.Campaign(new FakeRow(
            ("CampaignId", "1"), ("CampaignName", "X"), ("ImpressionSharePercent", "--")));

        Assert.Null(campaign.ImpressionShare);
        Assert.False(campaign.HasImpressionShareData);
    }

    [Fact]
    public void A_daily_row_needs_a_date_and_is_skipped_without_one()
    {
        Assert.NotNull(ReportRowMapper.Daily(new FakeRow(("TimePeriod", "2026-03-15"), ("Spend", "10"))));
        Assert.Null(ReportRowMapper.Daily(new FakeRow(("Spend", "10"))));
    }

    [Fact]
    public void A_search_term_row_maps_the_query_and_its_match_types()
    {
        var term = ReportRowMapper.SearchTerm(new FakeRow(
            ("SearchQuery", "boiler engineer jobs"), ("CampaignId", "1004"),
            ("CampaignName", "Generic"), ("AdGroupId", "55"), ("AdGroupName", "Discovery"),
            ("Keyword", "boiler engineer"), ("BidMatchType", "Broad"),
            ("DeliveredMatchType", "Broad"), ("Clicks", "18"), ("Spend", "24.55"),
            ("ConversionsQualified", "0")));

        Assert.NotNull(term);
        Assert.Equal("boiler engineer jobs", term!.SearchTerm);
        Assert.Equal(1004, term.CampaignId);
        Assert.Equal("boiler engineer", term.Keyword);
        Assert.True(term.Metrics.HasSpendButNoConversions);
    }

    [Fact]
    public void A_search_term_row_with_no_query_is_dropped_rather_than_shown_blank()
        => Assert.Null(ReportRowMapper.SearchTerm(new FakeRow(("Clicks", "5"))));

    [Fact]
    public void A_keyword_row_maps_quality_score_and_bid()
    {
        var keyword = ReportRowMapper.Keyword(new FakeRow(
            ("KeywordId", "2001"), ("Keyword", "gasfit heating"), ("BidMatchType", "Exact"),
            ("KeywordStatus", "Active"), ("CampaignName", "Brand"), ("AdGroupName", "Brand terms"),
            ("QualityScore", "9"), ("CurrentMaxCpc", "1.20"), ("Clicks", "40"), ("Spend", "22.40")));

        Assert.Equal(2001, keyword.KeywordId);
        Assert.Equal("Exact", keyword.MatchType);
        Assert.Equal(9m, keyword.QualityScore);
        Assert.Equal(1.20m, keyword.CurrentBid);
    }

    [Fact]
    public void A_change_row_maps_the_before_and_after_values()
    {
        var change = ReportRowMapper.Change(new FakeRow(
            ("DateTime", "2026-03-10 09:00:00"), ("ChangedBy", "someone@example.com"),
            ("ItemChanged", "Campaign"), ("EntityName", "Competitor"), ("CampaignId", "1003"),
            ("CampaignName", "Competitor"), ("AttributeChanged", "Daily budget"),
            ("HowChanged", "Changed"), ("OldValue", "60.00"), ("NewValue", "110.00")));

        Assert.NotNull(change);
        Assert.Equal("Daily budget", change!.AttributeChanged);
        Assert.Equal("60.00", change.OldValue);
        Assert.Equal("110.00", change.NewValue);
        Assert.Equal(new DateOnly(2026, 3, 10), change.ChangedOn);
        Assert.Contains("60.00", change.Describe());
        Assert.Contains("110.00", change.Describe());
    }

    [Fact]
    public void A_change_row_without_a_timestamp_is_dropped_rather_than_dated_arbitrarily()
        => Assert.Null(ReportRowMapper.Change(new FakeRow(("AttributeChanged", "Budget"))));

    [Fact]
    public void A_conflict_row_needs_both_sides_of_the_conflict()
    {
        Assert.NotNull(ReportRowMapper.Conflict(new FakeRow(
            ("Keyword", "boiler service cost"), ("NegativeKeyword", "cost"),
            ("ConflictLevel", "Campaign"), ("CampaignName", "Generic"))));

        Assert.Null(ReportRowMapper.Conflict(new FakeRow(("Keyword", "only one side"))));
    }
}
