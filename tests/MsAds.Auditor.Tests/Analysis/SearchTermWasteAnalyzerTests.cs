using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Settings;

namespace MsAds.Auditor.Tests.Analysis;

public class SearchTermWasteAnalyzerTests
{
    private readonly SearchTermWasteAnalyzer _analyzer = new();
    private readonly MetricFormatter _formatter = new("en-GB", "GBP");

    private static readonly AuditSettings Settings = new()
    {
        SearchTermMinimumSpend = 20m,
        SearchTermMinimumClicks = 10,
        TargetCpa = 50m,
        CpaToleranceAboveTargetPercent = 20m,
    };

    /// <summary>An account averaging a 4% conversion rate and a £2 CPC.</summary>
    private static readonly MetricSet Account = new()
    {
        Impressions = 100_000,
        Clicks = 5_000,
        Spend = 10_000m,
        Conversions = 200m,
        ConversionValue = 20_000m,
    };

    private static SearchTermPerformance Term(
        string text,
        long clicks,
        decimal spend,
        decimal? conversions,
        string? keyword = null,
        long impressions = 1000)
        => new()
        {
            SearchTerm = text,
            CampaignName = "Campaign",
            AdGroupName = "Ad group",
            Keyword = keyword,
            BidMatchType = "Phrase",
            DeliveredMatchType = "Broad",
            Metrics = new MetricSet
            {
                Impressions = impressions,
                Clicks = clicks,
                Spend = spend,
                Conversions = conversions,
                ConversionValue = conversions is null ? null : conversions.Value * 100m,
            },
        };

    private SearchTermAnalysis Analyse(params SearchTermPerformance[] terms)
        => _analyzer.Analyse(DataSet<SearchTermPerformance>.Of(terms), Settings, Account, _formatter, conversionDataAvailable: true);

    [Fact]
    public void A_term_spending_above_the_threshold_with_no_conversions_is_proposed_as_a_negative_keyword()
    {
        var analysis = Analyse(Term("boiler engineer jobs", clicks: 25, spend: 40m, conversions: 0m));

        var assessment = Assert.Single(analysis.Assessments);
        Assert.Contains(SearchTermFlag.SpendWithNoConversions, assessment.Flags);
        Assert.Equal(SearchTermAction.ConsiderNegativeKeyword, assessment.RecommendedAction);
        Assert.True(assessment.IsNegativeKeywordCandidate);
        Assert.Equal(40m, assessment.UnproductiveSpend);
        Assert.Equal(["boiler engineer jobs"], analysis.SuggestedNegativeKeywords);
    }

    [Fact]
    public void A_term_below_the_click_threshold_is_never_proposed_as_a_negative_keyword()
    {
        // Enough spend to flag, but only four clicks: not enough to justify blocking the query.
        var analysis = Analyse(Term("boiler installation sunday", clicks: 4, spend: 35m, conversions: 0m));

        var assessment = Assert.Single(analysis.Assessments);
        Assert.False(assessment.IsNegativeKeywordCandidate);
        Assert.Equal(SearchTermAction.InvestigateIntent, assessment.RecommendedAction);
        Assert.Empty(analysis.SuggestedNegativeKeywords);
    }

    [Fact]
    public void A_low_volume_term_is_reported_as_insufficient_evidence_and_left_to_monitor()
    {
        var analysis = Analyse(Term("vaillant boiler quote", clicks: 3, spend: 5m, conversions: 0m));

        var assessment = Assert.Single(analysis.Assessments);
        Assert.Contains(SearchTermFlag.InsufficientEvidence, assessment.Flags);
        Assert.Equal(SearchTermAction.ContinueMonitoring, assessment.RecommendedAction);
        Assert.False(assessment.IsFlagged);
        Assert.Equal(FindingConfidence.Low, assessment.Confidence);
    }

    [Fact]
    public void A_term_converting_well_below_the_account_average_is_sent_to_the_landing_page_and_tracking()
    {
        // 30 clicks, 1 conversion = 3.3% against the account's 4%... not low enough. Use 0.5 conversions.
        var analysis = Analyse(Term("emergency boiler repair near me", clicks: 60, spend: 90m, conversions: 0.6m));

        var assessment = Assert.Single(analysis.Assessments);
        Assert.Contains(SearchTermFlag.PoorConversionRate, assessment.Flags);
        Assert.Equal(SearchTermAction.CheckLandingPageAndTracking, assessment.RecommendedAction);
        Assert.False(assessment.IsNegativeKeywordCandidate);
    }

    [Fact]
    public void A_term_whose_cpa_exceeds_target_is_sent_to_the_keyword_and_match_type()
    {
        // 2 conversions on £140 is a £70 CPA against a £50 target with a £60 flag threshold.
        var analysis = Analyse(Term("cheap boiler cover uk", clicks: 30, spend: 140m, conversions: 2m));

        var assessment = Assert.Single(analysis.Assessments);
        Assert.Contains(SearchTermFlag.CpaAboveTarget, assessment.Flags);
        Assert.Equal(SearchTermAction.ReviewKeywordOrMatchType, assessment.RecommendedAction);
    }

    [Fact]
    public void Unusually_expensive_clicks_are_flagged_relative_to_the_account_average()
    {
        // £8 CPC against the account's £2.
        var analysis = Analyse(Term("britishgas boiler cover price", clicks: 12, spend: 96m, conversions: 1m));

        var assessment = Assert.Single(analysis.Assessments);
        Assert.Contains(SearchTermFlag.ExpensiveClicks, assessment.Flags);
    }

    [Fact]
    public void A_query_sharing_little_wording_with_its_keyword_is_flagged_with_an_explicit_caveat()
    {
        var analysis = Analyse(Term("plumber apprenticeship vacancies", clicks: 20, spend: 30m, conversions: 0m,
            keyword: "boiler installation"));

        var assessment = Assert.Single(analysis.Assessments);
        Assert.Contains(SearchTermFlag.LowKeywordOverlap, assessment.Flags);
        Assert.Contains(assessment.Caveats, c => c.Contains("Overlap is measured on words alone"));
    }

    [Theory]
    [InlineData("new boiler installation cost", "boiler installation", true)]
    [InlineData("plumber apprenticeship vacancies", "boiler installation", false)]
    public void Keyword_overlap_ignores_filler_words_when_measuring_similarity(
        string query, string keyword, bool expectedHighOverlap)
    {
        var overlap = SearchTermWasteAnalyzer.KeywordOverlap(query, keyword);

        Assert.NotNull(overlap);
        Assert.Equal(expectedHighOverlap, overlap!.Value >= 0.34m);
    }

    [Fact]
    public void Keyword_overlap_is_null_when_there_is_no_keyword_to_compare_against()
        => Assert.Null(SearchTermWasteAnalyzer.KeywordOverlap("anything", null));

    [Fact]
    public void Without_conversion_data_no_term_is_proposed_as_a_negative_keyword()
    {
        var analysis = _analyzer.Analyse(
            DataSet<SearchTermPerformance>.Of([Term("boiler engineer jobs", clicks: 50, spend: 200m, conversions: null)]),
            Settings, Account, _formatter, conversionDataAvailable: false);

        var assessment = Assert.Single(analysis.Assessments);
        Assert.False(assessment.IsNegativeKeywordCandidate);
        Assert.Equal(SearchTermAction.InvestigateIntent, assessment.RecommendedAction);
        Assert.Contains(assessment.Caveats, c => c.Contains("Conversion data was unavailable"));
        Assert.Contains(analysis.Notices, n => n.Code == DataNotices.ConversionDataUnavailableCode);
    }

    [Fact]
    public void The_coverage_warning_is_always_attached_so_absence_is_never_read_as_proof()
    {
        var analysis = Analyse(Term("anything", clicks: 1, spend: 1m, conversions: 0m));

        Assert.Contains(analysis.Notices, n => n.Code == DataNotices.SearchTermCoverageCode);
    }

    [Fact]
    public void Unavailable_search_term_data_is_distinct_from_finding_nothing_to_flag()
    {
        var unavailable = _analyzer.Analyse(
            DataSet<SearchTermPerformance>.Unavailable(DataNotice.Error("boom", "Report failed.")),
            Settings, Account, _formatter, conversionDataAvailable: true);

        Assert.True(unavailable.DataUnavailable);
        Assert.Empty(unavailable.Assessments);

        var empty = _analyzer.Analyse(
            DataSet<SearchTermPerformance>.Of([]), Settings, Account, _formatter, conversionDataAvailable: true);

        Assert.False(empty.DataUnavailable);
        Assert.Equal(0, empty.TermCount);
    }

    [Fact]
    public void Totals_separate_spend_analysed_from_spend_that_produced_nothing()
    {
        var analysis = Analyse(
            Term("converting term", clicks: 40, spend: 80m, conversions: 4m),
            Term("wasteful term", clicks: 30, spend: 60m, conversions: 0m));

        Assert.Equal(140m, analysis.SpendAnalysed);
        Assert.Equal(60m, analysis.UnproductiveSpend);
        Assert.Equal(2, analysis.TermCount);
    }
}
