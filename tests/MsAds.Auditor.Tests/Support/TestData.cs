using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.DependencyInjection;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Tests.Support;

/// <summary>
/// Builders for the objects the rules read, so each test states only the values it cares about.
/// </summary>
internal static class TestData
{
    public static readonly DateTimeOffset FixedNow = new(2026, 3, 16, 10, 30, 0, TimeSpan.Zero);

    public static MetricSet Metrics(
        long impressions = 1000,
        long clicks = 50,
        decimal spend = 100m,
        decimal? conversions = 5m,
        decimal? conversionValue = 500m)
        => new()
        {
            Impressions = impressions,
            Clicks = clicks,
            Spend = spend,
            Conversions = conversions,
            ConversionValue = conversionValue,
        };

    public static ComparedEntity Campaign(
        string name = "Test campaign",
        long id = 1,
        MetricSet? current = null,
        MetricSet? previous = null,
        string? status = "Active",
        decimal? impressionShare = null,
        decimal? lostToBudget = null,
        decimal? lostToRank = null)
        => new()
        {
            EntityType = "Campaign",
            EntityId = id,
            Name = name,
            Status = status,
            Current = current ?? Metrics(),
            Previous = previous,
            ImpressionShare = impressionShare,
            ImpressionShareLostToBudget = lostToBudget,
            ImpressionShareLostToRank = lostToRank,
        };

    public static ResolvedPeriod Period(int days = 30, bool withComparison = true)
    {
        var end = new DateOnly(2026, 3, 15);
        var current = new DateRange(end.AddDays(-(days - 1)), end);

        return new ResolvedPeriod
        {
            Period = days switch
            {
                7 => ReportingPeriod.Last7Days,
                14 => ReportingPeriod.Last14Days,
                _ => ReportingPeriod.Last30Days,
            },
            Current = current,
            Previous = withComparison ? current.PrecedingPeriod() : null,
            AccountToday = new DateOnly(2026, 3, 16),
            TimeZoneId = "UTC",
            CurrentDayExcluded = true,
        };
    }

    public static AuditContext Context(
        MetricSet? accountCurrent = null,
        MetricSet? accountPrevious = null,
        IReadOnlyList<ComparedEntity>? campaigns = null,
        IReadOnlyList<ComparedEntity>? keywords = null,
        AuditSettings? settings = null,
        bool conversionDataUnavailable = false,
        IReadOnlyList<DailyMetrics>? dailyCurrent = null,
        IReadOnlyList<ChangeRecord>? changes = null,
        IReadOnlyList<NegativeKeywordConflict>? conflicts = null)
    {
        var resolved = settings ?? new AuditSettings();

        return new AuditContext
        {
            Scope = new ReportScope { AccountId = 42, CustomerId = 7, Range = Period().Current },
            Period = Period(),
            Settings = resolved,
            Formatter = new MetricFormatter("en-GB", "GBP"),
            AccountCurrent = accountCurrent ?? Metrics(spend: 1000m, clicks: 500, conversions: 50m),
            AccountPrevious = accountPrevious,
            Campaigns = campaigns ?? [],
            Keywords = keywords ?? [],
            DailyCurrent = dailyCurrent ?? [],
            Changes = changes ?? [],
            NegativeKeywordConflicts = conflicts ?? [],
            ConversionDataUnavailable = conversionDataUnavailable,
        };
    }

    /// <summary>
    /// Resolves the engine through the real dependency injection registration, so the tests also
    /// cover the fact that every rule is registered and constructible.
    /// </summary>
    public static RecommendationEngine Engine()
    {
        var services = new ServiceCollection();
        services.AddAuditorCore();
        return services.BuildServiceProvider().GetRequiredService<RecommendationEngine>();
    }

    public static AuditReportBuilder Builder(IClock? clock = null)
        => new(
            Engine(),
            new SearchTermWasteAnalyzer(),
            new ExecutiveSummaryGenerator(),
            clock ?? new FixedClock(FixedNow),
            NullLogger<AuditReportBuilder>.Instance);
}
