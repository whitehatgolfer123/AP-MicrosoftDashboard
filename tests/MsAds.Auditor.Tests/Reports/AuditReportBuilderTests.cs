using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Demo;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Tests.Support;

namespace MsAds.Auditor.Tests.Reports;

public class AuditReportBuilderTests
{
    private static readonly FixedClock Clock = new(TestData.FixedNow);

    private static ResolvedPeriod Period(ReportingPeriod period = ReportingPeriod.Last30Days)
        => new ReportingPeriodResolver(Clock).Resolve(period, TimeZoneInfo.Utc);

    private static Task<AuditReport> BuildAsync(
        IAdsDataSource source,
        AuditSettings? settings = null,
        AnalysisSelection? selection = null,
        ReportingPeriod period = ReportingPeriod.Last30Days)
        => TestData.Builder(Clock).BuildAsync(
            source,
            DemoAccounts.Primary,
            Period(period),
            settings ?? new AuditSettings(),
            selection ?? AnalysisSelection.All);

    /// <summary>A data source whose every dataset can be set to empty, unavailable, or supplied.</summary>
    private sealed class FakeDataSource : IAdsDataSource
    {
        public DataSourceKind Kind { get; init; } = DataSourceKind.Live;
        public string DisplayName => "Fake";

        public DataSet<DailyMetrics> Daily { get; set; } = new();
        public DataSet<CampaignPerformance> Campaigns { get; set; } = new();
        public DataSet<SearchTermPerformance> SearchTerms { get; set; } = new();
        public DataSet<KeywordPerformance> Keywords { get; set; } = new();
        public DataSet<ChangeRecord> Changes { get; set; } = new();
        public DataSet<NegativeKeywordConflict> Conflicts { get; set; } = new();

        public List<string> Requested { get; } = [];

        public Task<DataSet<DailyMetrics>> GetDailyMetricsAsync(ReportScope s, CancellationToken c = default)
        { Requested.Add($"daily:{s.Range}"); return Task.FromResult(Daily); }

        public Task<DataSet<CampaignPerformance>> GetCampaignPerformanceAsync(ReportScope s, CancellationToken c = default)
        { Requested.Add($"campaigns:{s.Range}"); return Task.FromResult(Campaigns); }

        public Task<DataSet<SearchTermPerformance>> GetSearchTermsAsync(ReportScope s, CancellationToken c = default)
        { Requested.Add($"searchterms:{s.Range}"); return Task.FromResult(SearchTerms); }

        public Task<DataSet<KeywordPerformance>> GetKeywordPerformanceAsync(ReportScope s, CancellationToken c = default)
        { Requested.Add($"keywords:{s.Range}"); return Task.FromResult(Keywords); }

        public Task<DataSet<ChangeRecord>> GetChangeHistoryAsync(ReportScope s, CancellationToken c = default)
        { Requested.Add($"changes:{s.Range}"); return Task.FromResult(Changes); }

        public Task<DataSet<NegativeKeywordConflict>> GetNegativeKeywordConflictsAsync(ReportScope s, CancellationToken c = default)
        { Requested.Add($"conflicts:{s.Range}"); return Task.FromResult(Conflicts); }
    }

    private static DataSet<DailyMetrics> DailyWith(decimal spendPerDay, decimal? conversionsPerDay, DateRange range)
        => DataSet<DailyMetrics>.Of([.. range.EachDay().Select(d => new DailyMetrics(d, new MetricSet
        {
            Impressions = 1000,
            Clicks = 50,
            Spend = spendPerDay,
            Conversions = conversionsPerDay,
            ConversionValue = conversionsPerDay * 100m,
        }))]);

    [Fact]
    public async Task A_demo_report_is_marked_as_demo_and_carries_a_prominent_warning()
    {
        var report = await BuildAsync(new DemoDataSource(Clock));

        Assert.True(report.IsDemo);
        Assert.Contains(report.Notices, n =>
            n.Code == DataNotices.DemoDataCode && n.Severity == NoticeSeverity.Warning);
    }

    [Fact]
    public async Task A_live_report_carries_no_demo_warning()
    {
        var source = new FakeDataSource { Daily = DailyWith(100m, 2m, Period().Current) };

        var report = await BuildAsync(source);

        Assert.False(report.IsDemo);
        Assert.DoesNotContain(report.Notices, n => n.Code == DataNotices.DemoDataCode);
    }

    [Fact]
    public async Task Both_periods_are_requested_and_they_do_not_overlap()
    {
        var source = new FakeDataSource();

        await BuildAsync(source);

        var dailyRequests = source.Requested.Where(r => r.StartsWith("daily:")).ToList();

        Assert.Equal(2, dailyRequests.Count);
        Assert.Contains("daily:2026-02-14 to 2026-03-15", dailyRequests);
        Assert.Contains("daily:2026-01-15 to 2026-02-13", dailyRequests);
    }

    [Fact]
    public async Task Deselected_analyses_are_not_requested_at_all()
    {
        var source = new FakeDataSource();

        await BuildAsync(source, selection: new AnalysisSelection
        {
            Campaigns = true,
            SearchTerms = false,
            Keywords = false,
            ChangeHistory = false,
            NegativeKeywordConflicts = false,
        });

        Assert.Contains(source.Requested, r => r.StartsWith("campaigns:"));
        Assert.DoesNotContain(source.Requested, r => r.StartsWith("searchterms:"));
        Assert.DoesNotContain(source.Requested, r => r.StartsWith("keywords:"));
        Assert.DoesNotContain(source.Requested, r => r.StartsWith("changes:"));
        Assert.DoesNotContain(source.Requested, r => r.StartsWith("conflicts:"));
    }

    [Fact]
    public async Task An_empty_reporting_period_is_reported_as_empty_rather_than_failing()
    {
        var report = await BuildAsync(new FakeDataSource());

        Assert.True(report.Current.IsEmpty);
        Assert.Contains(report.Notices, n => n.Code == DataNotices.EmptyPeriodCode);
        Assert.Contains("nothing to compare", report.ExecutiveSummary.Headline);
    }

    [Fact]
    public async Task Missing_conversion_columns_are_detected_and_conversion_rules_suppressed()
    {
        var range = Period().Current;
        var source = new FakeDataSource { Daily = DailyWith(100m, conversionsPerDay: null, range) };

        var report = await BuildAsync(source);

        Assert.True(report.ConversionDataUnavailable);
        Assert.Null(report.Current.Conversions);
        Assert.Contains(report.Notices, n => n.Code == DataNotices.ConversionDataUnavailableCode);

        // The tracking rule explains the gap; no rule claims zero conversions.
        Assert.Contains(report.Recommendations.Findings, f => f.RuleId == "account-conversion-tracking");
        Assert.DoesNotContain(report.Recommendations.Findings, f => f.RuleId.EndsWith("-zero-conversions"));
    }

    [Fact]
    public async Task Conversion_data_present_on_only_some_rows_still_counts_as_available()
    {
        var range = Period().Current;
        var rows = range.EachDay().Select((d, i) => new DailyMetrics(d, new MetricSet
        {
            Impressions = 1000,
            Clicks = 50,
            Spend = 100m,
            // Only the first day reports a conversion value; the rest are genuine zeros.
            Conversions = i == 0 ? 3m : 0m,
        })).ToList();

        var report = await BuildAsync(new FakeDataSource { Daily = DataSet<DailyMetrics>.Of(rows) });

        Assert.False(report.ConversionDataUnavailable);
        Assert.Equal(3m, report.Current.Conversions);
    }

    [Fact]
    public async Task An_unavailable_dataset_is_distinguished_from_an_empty_one()
    {
        var range = Period().Current;
        var source = new FakeDataSource
        {
            Daily = DailyWith(100m, 2m, range),
            SearchTerms = DataSet<SearchTermPerformance>.Unavailable(
                DataNotice.Error("search-term-failed", "The search query report could not be downloaded.")),
            Changes = DataSet<ChangeRecord>.Unavailable(
                DataNotice.Error("changes-failed", "Change history could not be retrieved.")),
        };

        var report = await BuildAsync(source);

        Assert.True(report.SearchTerms.DataUnavailable);
        Assert.True(report.ChangeHistoryUnavailable);
        Assert.Contains(report.ExecutiveSummary.Caveats, c => c.Contains("Search term data could not be retrieved"));
        Assert.Contains(report.ExecutiveSummary.Caveats, c => c.Contains("Change history could not be retrieved"));
        Assert.Contains(report.Notices, n => n.Severity == NoticeSeverity.Error);
    }

    [Fact]
    public async Task The_report_records_the_exact_dates_and_time_zone_used()
    {
        var report = await BuildAsync(new DemoDataSource(Clock), period: ReportingPeriod.Last7Days);

        Assert.Equal(new DateOnly(2026, 3, 9), report.Period.Current.Start);
        Assert.Equal(new DateOnly(2026, 3, 15), report.Period.Current.End);
        Assert.Equal(new DateOnly(2026, 3, 2), report.Period.Previous!.Value.Start);
        Assert.True(report.Period.CurrentDayExcluded);
        Assert.Contains(report.Notices, n => n.Code == DataNotices.PartialDayExcludedCode);
    }

    [Fact]
    public async Task The_accounts_own_currency_overrides_the_configured_default()
    {
        var report = await BuildAsync(
            new DemoDataSource(Clock),
            settings: new AuditSettings { CurrencyCode = "USD" });

        // The demo account reports in GBP, so GBP must win.
        Assert.Equal("GBP", report.Formatter.CurrencyCode);
    }

    [Fact]
    public async Task Campaigns_that_ran_only_in_the_previous_period_are_still_listed()
    {
        var report = await BuildAsync(new DemoDataSource(Clock), period: ReportingPeriod.Last7Days);

        var stopped = report.Campaigns.SingleOrDefault(c => c.EntityId == 1006);

        Assert.NotNull(stopped);
        Assert.Equal(0m, stopped!.Current.Spend);
        Assert.NotNull(stopped.Previous);
        Assert.True(stopped.Previous!.Spend > 0m);
    }

    [Fact]
    public async Task A_demo_audit_finds_the_planted_problems_and_the_planted_successes()
    {
        var report = await BuildAsync(
            new DemoDataSource(Clock),
            settings: new AuditSettings { TargetCpa = 60m, MinimumSpend = 50m });

        var ruleIds = report.Recommendations.Findings.Select(f => f.RuleId).ToList();

        Assert.Contains("campaign-zero-conversions", ruleIds);
        Assert.Contains("campaign-spend-up-conversions-down", ruleIds);
        Assert.Contains("campaign-delivery-change", ruleIds);
        Assert.Contains("campaign-ctr-decline", ruleIds);
        Assert.Contains("campaign-impression-share-lost-to-budget", ruleIds);
        Assert.Contains("campaign-strong-performer", ruleIds);
        Assert.Contains("negative-keyword-conflict", ruleIds);

        Assert.Contains(report.Recommendations.Findings, f => f.Severity == FindingSeverity.Positive);
        Assert.Empty(report.Recommendations.RuleFailures);
        Assert.NotEmpty(report.SearchTerms.SuggestedNegativeKeywords);
    }

    [Fact]
    public async Task Without_targets_the_summary_says_so_rather_than_inventing_an_objective()
    {
        var report = await BuildAsync(new DemoDataSource(Clock), settings: new AuditSettings
        {
            TargetCpa = null,
            TargetRoas = null,
        });

        Assert.Contains(report.ExecutiveSummary.Caveats, c => c.Contains("No target CPA or ROAS is configured"));
    }

    [Fact]
    public async Task The_summary_cards_cover_every_catalogued_metric()
    {
        var report = await BuildAsync(new DemoDataSource(Clock));

        Assert.Equal(MetricCatalogue.DashboardOrder.Count, report.Summary.Count);
        Assert.All(report.Summary, card => Assert.NotNull(card.Descriptor));
    }
}
