using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Demo;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Tests.Support;

namespace MsAds.Auditor.Tests.Demo;

public class DemoDataSourceTests
{
    private static readonly FixedClock Clock = new(TestData.FixedNow);

    private static ReportScope Scope(DateOnly start, DateOnly end) => new()
    {
        AccountId = DemoAccounts.PrimaryAccountId,
        CustomerId = 9900001,
        Range = new DateRange(start, end),
        CurrencyCode = "GBP",
    };

    /// <summary>The 7 days ending yesterday, relative to the fixed clock.</summary>
    private static ReportScope CurrentScope => Scope(new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15));

    private static ReportScope PreviousScope => Scope(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8));

    [Fact]
    public void The_source_identifies_itself_as_demo_so_reports_can_be_labelled()
    {
        var source = new DemoDataSource(Clock);

        Assert.Equal(DataSourceKind.Demo, source.Kind);
        Assert.Contains("Demo", source.DisplayName);
    }

    [Fact]
    public async Task The_same_request_always_returns_the_same_figures()
    {
        var first = await new DemoDataSource(Clock).GetDailyMetricsAsync(CurrentScope);
        var second = await new DemoDataSource(Clock).GetDailyMetricsAsync(CurrentScope);

        Assert.Equal(first.Rows.Count, second.Rows.Count);
        Assert.Equal(
            first.Rows.Select(r => r.Metrics.Spend),
            second.Rows.Select(r => r.Metrics.Spend));
    }

    [Fact]
    public async Task Daily_metrics_cover_every_day_of_the_requested_range()
    {
        var daily = await new DemoDataSource(Clock).GetDailyMetricsAsync(CurrentScope);

        Assert.Equal(7, daily.Rows.Count);
        Assert.Equal(new DateOnly(2026, 3, 9), daily.Rows[0].Date);
        Assert.Equal(new DateOnly(2026, 3, 15), daily.Rows[^1].Date);
    }

    [Fact]
    public async Task Campaign_totals_reconcile_with_the_daily_totals()
    {
        var source = new DemoDataSource(Clock);
        var daily = await source.GetDailyMetricsAsync(CurrentScope);
        var campaigns = await source.GetCampaignPerformanceAsync(CurrentScope);

        var dailySpend = daily.Rows.Sum(d => d.Metrics.Spend);
        var campaignSpend = campaigns.Rows.Sum(c => c.Metrics.Spend);

        // The charts and the campaign table must not disagree, or the demo looks broken.
        Assert.Equal(dailySpend, campaignSpend);
    }

    [Fact]
    public async Task The_current_and_comparison_periods_differ_so_changes_are_visible()
    {
        var source = new DemoDataSource(Clock);
        var current = await source.GetCampaignPerformanceAsync(CurrentScope);
        var previous = await source.GetCampaignPerformanceAsync(PreviousScope);

        var currentCompetitor = current.Rows.Single(c => c.CampaignId == 1003);
        var previousCompetitor = previous.Rows.Single(c => c.CampaignId == 1003);

        // The planted scenario: spend up, conversions down.
        Assert.True(currentCompetitor.Metrics.Spend > previousCompetitor.Metrics.Spend);
        Assert.True(currentCompetitor.Metrics.Conversions < previousCompetitor.Metrics.Conversions);
    }

    [Fact]
    public async Task The_campaign_planted_as_stopped_delivers_nothing_in_the_current_period()
    {
        var source = new DemoDataSource(Clock);
        var current = await source.GetCampaignPerformanceAsync(CurrentScope);
        var previous = await source.GetCampaignPerformanceAsync(PreviousScope);

        Assert.DoesNotContain(current.Rows, c => c.CampaignId == 1006);
        Assert.Contains(previous.Rows, c => c.CampaignId == 1006 && c.Metrics.Impressions > 500);
    }

    [Fact]
    public async Task Impression_share_is_only_reported_for_the_current_period()
    {
        var source = new DemoDataSource(Clock);

        var current = await source.GetCampaignPerformanceAsync(CurrentScope);
        var previous = await source.GetCampaignPerformanceAsync(PreviousScope);

        Assert.Contains(current.Rows, c => c.HasImpressionShareData);
        Assert.All(previous.Rows, c => Assert.False(c.HasImpressionShareData));
    }

    [Fact]
    public async Task Search_terms_arrive_with_the_coverage_warning_attached()
    {
        var terms = await new DemoDataSource(Clock).GetSearchTermsAsync(CurrentScope);

        Assert.NotEmpty(terms.Rows);
        Assert.Contains(terms.Notices, n => n.Code == DataNotices.SearchTermCoverageCode);
    }

    [Fact]
    public async Task Change_records_fall_inside_the_requested_period()
    {
        var changes = await new DemoDataSource(Clock).GetChangeHistoryAsync(CurrentScope);

        Assert.NotEmpty(changes.Rows);
        Assert.All(changes.Rows, c => Assert.True(CurrentScope.Range.Contains(c.ChangedOn)));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    public async Task Every_selectable_period_produces_usable_data(int days)
    {
        var end = new DateOnly(2026, 3, 15);
        var scope = Scope(end.AddDays(-(days - 1)), end);
        var source = new DemoDataSource(Clock);

        var daily = await source.GetDailyMetricsAsync(scope);
        var campaigns = await source.GetCampaignPerformanceAsync(scope);
        var terms = await source.GetSearchTermsAsync(scope);
        var keywords = await source.GetKeywordPerformanceAsync(scope);

        Assert.Equal(days, daily.Rows.Count);
        Assert.NotEmpty(campaigns.Rows);
        Assert.NotEmpty(terms.Rows);
        Assert.NotEmpty(keywords.Rows);
    }
}
