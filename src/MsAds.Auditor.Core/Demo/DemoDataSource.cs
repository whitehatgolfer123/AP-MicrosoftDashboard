using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.Demo;

/// <summary>
/// Generates synthetic advertising data so the whole application can be built, demonstrated and
/// tested before API access is configured. Deterministic: the same account and dates always
/// produce the same figures.
/// </summary>
/// <remarks>
/// <para>
/// This is never live data. <see cref="Kind"/> returns <see cref="DataSourceKind.Demo"/>, which
/// causes the report builder to attach a prominent demo warning to every report and export.
/// </para>
/// <para>
/// The generator works from per-day rates that differ between the current and the comparison
/// period, so each planted scenario holds for any period length the user selects. Which period a
/// request belongs to is inferred from its end date: the current period is the one ending at or
/// after yesterday.
/// </para>
/// </remarks>
public sealed class DemoDataSource(IClock clock) : IAdsDataSource
{
    /// <summary>Assumed click-through rates by match type, used to derive search term impressions.</summary>
    private static decimal AssumedCtr(string matchType) => matchType switch
    {
        "Exact" => 9.2m,
        "Phrase" => 4.8m,
        _ => 2.4m,
    };

    public DataSourceKind Kind => DataSourceKind.Demo;

    public string DisplayName => "Demo data (synthetic)";

    public Task<DataSet<DailyMetrics>> GetDailyMetricsAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
    {
        var rows = scope.Range.EachDay()
            .Select(day => new DailyMetrics(
                day,
                MetricSet.Sum(DemoScenario.Campaigns.Select(spec => DayMetrics(spec, scope, day)))))
            .ToList();

        return Task.FromResult(DataSet<DailyMetrics>.Of(rows));
    }

    public Task<DataSet<CampaignPerformance>> GetCampaignPerformanceAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
    {
        var isCurrent = IsCurrentPeriod(scope);

        var rows = DemoScenario.Campaigns
            .Select(spec => new CampaignPerformance
            {
                CampaignId = spec.Id,
                CampaignName = spec.Name,
                Status = spec.Status,
                CampaignType = spec.Type,
                // Summing the same daily figures the charts use keeps the two consistent.
                Metrics = MetricSet.Sum(scope.Range.EachDay().Select(day => DayMetrics(spec, scope, day))),
                ImpressionShare = isCurrent ? spec.ImpressionShare : null,
                ImpressionShareLostToBudget = isCurrent ? spec.ImpressionShareLostToBudget : null,
                ImpressionShareLostToRank = isCurrent ? spec.ImpressionShareLostToRank : null,
            })
            .Where(c => c.Metrics.Impressions > 0 || c.Metrics.Spend > 0m)
            .ToList();

        return Task.FromResult(DataSet<CampaignPerformance>.Of(rows));
    }

    public Task<DataSet<SearchTermPerformance>> GetSearchTermsAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
    {
        var days = scope.Range.Days;
        var isCurrent = IsCurrentPeriod(scope);
        var campaignNames = DemoScenario.Campaigns.ToDictionary(c => c.Id, c => c.Name);
        var rows = new List<SearchTermPerformance>();

        foreach (var spec in DemoScenario.SearchTerms)
        {
            var random = new DeterministicRandom(
                DeterministicRandom.Seed("searchterm", scope.AccountId, spec.Term, scope.Range.Start));

            // Scale the 30-day click figure to the selected period, and run the comparison period
            // slightly softer so period-on-period movement is visible.
            var scale = (decimal)days / 30m * (isCurrent ? 1m : 0.88m);
            var clicks = (long)Math.Round(random.Jitter(spec.ClicksPerPeriodAt30Days * scale, 0.10));

            if (clicks <= 0)
            {
                continue;
            }

            var cpc = random.Jitter(spec.Cpc, 0.06);
            var spend = Math.Round(clicks * cpc, 2);
            var impressions = (long)Math.Round(clicks / (AssumedCtr(spec.MatchType) / 100m));
            var conversions = Math.Round(clicks * spec.ConversionRatePercent / 100m, 2);

            rows.Add(new SearchTermPerformance
            {
                SearchTerm = spec.Term,
                CampaignId = spec.CampaignId,
                CampaignName = campaignNames.GetValueOrDefault(spec.CampaignId),
                AdGroupId = spec.CampaignId * 10,
                AdGroupName = spec.AdGroup,
                Keyword = spec.Keyword,
                BidMatchType = spec.MatchType,
                DeliveredMatchType = spec.MatchType == "Exact" ? "Exact" : "Broad",
                Metrics = new MetricSet
                {
                    Impressions = impressions,
                    Clicks = clicks,
                    Spend = spend,
                    Conversions = conversions,
                    ConversionValue = Math.Round(conversions * 88m, 2),
                },
            });
        }

        return Task.FromResult(DataSet<SearchTermPerformance>.Of(
            [.. rows.OrderByDescending(r => r.Metrics.Spend)],
            DataNotices.SearchTermCoverage()));
    }

    public Task<DataSet<KeywordPerformance>> GetKeywordPerformanceAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
    {
        var days = scope.Range.Days;
        var isCurrent = IsCurrentPeriod(scope);
        var rows = new List<KeywordPerformance>();

        foreach (var spec in DemoScenario.Keywords)
        {
            var random = new DeterministicRandom(
                DeterministicRandom.Seed("keyword", scope.AccountId, spec.Id, scope.Range.Start));

            // Competitor keywords are planted with a sharp CPC rise and a falling conversion rate.
            var isCompetitor = spec.CampaignId == 1003;
            var cpc = isCurrent ? spec.Cpc : spec.Cpc * (isCompetitor ? 0.74m : 0.96m);
            var conversionRate = isCurrent
                ? spec.ConversionRatePercent
                : spec.ConversionRatePercent * (isCompetitor ? 2.2m : 1.08m);

            var clicks = (long)Math.Round(random.Jitter(spec.ClicksPerDay * days, 0.08));

            if (clicks <= 0)
            {
                continue;
            }

            var spend = Math.Round(clicks * random.Jitter(cpc, 0.05), 2);
            var impressions = (long)Math.Round(clicks / (spec.CtrPercent / 100m));
            var conversions = Math.Round(clicks * conversionRate / 100m, 2);

            rows.Add(new KeywordPerformance
            {
                KeywordId = spec.Id,
                Keyword = spec.Keyword,
                MatchType = spec.MatchType,
                CampaignId = spec.CampaignId,
                CampaignName = spec.CampaignName,
                AdGroupId = spec.CampaignId * 10,
                AdGroupName = spec.AdGroup,
                Status = "Active",
                QualityScore = spec.QualityScore,
                CurrentBid = Math.Round(cpc * 1.15m, 2),
                Metrics = new MetricSet
                {
                    Impressions = impressions,
                    Clicks = clicks,
                    Spend = spend,
                    Conversions = conversions,
                    ConversionValue = Math.Round(conversions * 86m, 2),
                },
            });
        }

        return Task.FromResult(DataSet<KeywordPerformance>.Of([.. rows.OrderByDescending(r => r.Metrics.Spend)]));
    }

    public Task<DataSet<ChangeRecord>> GetChangeHistoryAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
    {
        // Changes are placed proportionally through the period so they land inside any selected length.
        var range = scope.Range;
        var days = range.Days;

        DateTimeOffset At(double fraction, int hour)
        {
            var offset = Math.Min(days - 1, Math.Max(0, (int)Math.Round(fraction * (days - 1))));
            var date = range.Start.AddDays(offset);
            return new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
        }

        var rows = new List<ChangeRecord>
        {
            new()
            {
                ChangedAt = At(0.30, 9),
                ChangedBy = "demo.user@example.com",
                EntityType = "Campaign",
                EntityName = "Competitor – Conquesting",
                CampaignId = 1003,
                CampaignName = "Competitor – Conquesting",
                AttributeChanged = "Daily budget",
                HowChanged = "Changed",
                OldValue = "60.00",
                NewValue = "110.00",
            },
            new()
            {
                ChangedAt = At(0.32, 9),
                ChangedBy = "demo.user@example.com",
                EntityType = "Campaign",
                EntityName = "Competitor – Conquesting",
                CampaignId = 1003,
                CampaignName = "Competitor – Conquesting",
                AttributeChanged = "Bid strategy",
                HowChanged = "Changed",
                OldValue = "ManualCpc",
                NewValue = "MaxClicks",
            },
            new()
            {
                ChangedAt = At(0.45, 14),
                ChangedBy = "demo.user@example.com",
                EntityType = "Campaign",
                EntityName = "Remarketing – Site visitors",
                CampaignId = 1006,
                CampaignName = "Remarketing – Site visitors",
                AttributeChanged = "Status",
                HowChanged = "Changed",
                OldValue = "Active",
                NewValue = "Paused",
            },
            new()
            {
                ChangedAt = At(0.55, 11),
                ChangedBy = "agency.team@example.com",
                EntityType = "Keyword",
                EntityName = "boiler engineer",
                CampaignId = 1004,
                CampaignName = "Generic – Broad discovery",
                AdGroupName = "Discovery",
                AttributeChanged = "Keyword",
                HowChanged = "Added",
                OldValue = null,
                NewValue = "boiler engineer (Broad)",
            },
            new()
            {
                ChangedAt = At(0.62, 16),
                ChangedBy = "agency.team@example.com",
                EntityType = "Negative keyword",
                EntityName = "jobs",
                CampaignId = 1002,
                CampaignName = "Generic – Core services",
                AttributeChanged = "Negative keyword",
                HowChanged = "Added",
                OldValue = null,
                NewValue = "jobs (Phrase)",
            },
            new()
            {
                ChangedAt = At(0.78, 10),
                ChangedBy = "demo.user@example.com",
                EntityType = "Campaign",
                EntityName = "Brand – Exact",
                CampaignId = 1001,
                CampaignName = "Brand – Exact",
                AttributeChanged = "Daily budget",
                HowChanged = "Changed",
                OldValue = "45.00",
                NewValue = "42.00",
            },
        };

        return Task.FromResult(DataSet<ChangeRecord>.Of(
            [.. rows.Where(r => range.Contains(r.ChangedOn))]));
    }

    public Task<DataSet<NegativeKeywordConflict>> GetNegativeKeywordConflictsAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NegativeKeywordConflict> rows =
        [
            new()
            {
                CampaignId = 1002,
                CampaignName = "Generic – Core services",
                AdGroupName = "Servicing",
                Keyword = "boiler service cost",
                NegativeKeyword = "cost",
                ConflictLevel = "Campaign",
                NegativeKeywordListName = "Research exclusions",
            },
            new()
            {
                CampaignId = 1002,
                CampaignName = "Generic – Core services",
                AdGroupName = "Installation",
                Keyword = "boiler finance",
                NegativeKeyword = "finance",
                ConflictLevel = "Ad group",
            },
            new()
            {
                CampaignId = 1001,
                CampaignName = "Brand – Exact",
                AdGroupName = "Brand terms",
                Keyword = "gasfit boiler service",
                NegativeKeyword = "service",
                ConflictLevel = "Campaign",
                NegativeKeywordListName = "Research exclusions",
            },
        ];

        return Task.FromResult(DataSet<NegativeKeywordConflict>.Of(rows));
    }

    /// <summary>
    /// One campaign's figures for one day, derived from its per-period rates with weekday
    /// weighting and a small deterministic jitter.
    /// </summary>
    private MetricSet DayMetrics(DemoCampaignSpec spec, ReportScope scope, DateOnly day)
    {
        var isCurrent = IsCurrentPeriod(scope);
        var rates = isCurrent ? spec.Current : spec.Previous;

        // Each demo account differs from the baseline, so a portfolio scan shows a realistic
        // spread of improving, declining, stalled and untracked accounts.
        var profile = DemoAccountProfile.For(scope.AccountId);
        var spendFactor = isCurrent ? profile.CurrentSpendFactor : profile.PreviousSpendFactor;
        var conversionFactor = isCurrent ? profile.CurrentConversionRateFactor : 1m;

        var spendPerDay = rates.SpendPerDay * spendFactor;

        if (spendPerDay <= 0m || rates.Cpc <= 0m)
        {
            // A campaign that did not run still reports zero conversions rather than no data,
            // because the report covered it and found nothing — unless the account has no
            // conversion tracking at all, in which case the column is genuinely absent.
            return MetricSet.Empty with
            {
                Conversions = profile.ReportsConversions ? 0m : null,
                ConversionValue = profile.ReportsConversions ? 0m : null,
            };
        }

        var random = new DeterministicRandom(DeterministicRandom.Seed("daily", scope.AccountId, spec.Id, day));

        var weight = DemoScenario.DayWeight(day.DayOfWeek);
        var spend = Math.Round(random.Jitter(spendPerDay * weight, 0.12), 2);
        var clicks = (long)Math.Round(spend / random.Jitter(rates.Cpc, 0.05));
        var impressions = (long)Math.Round(clicks / (random.Jitter(rates.CtrPercent, 0.08) / 100m));
        var conversions = Math.Round(clicks * rates.ConversionRatePercent * conversionFactor / 100m, 2);

        return new MetricSet
        {
            Impressions = Math.Max(impressions, clicks),
            Clicks = clicks,
            Spend = spend,
            // An account without conversion tracking returns no conversion columns at all.
            Conversions = profile.ReportsConversions ? conversions : null,
            ConversionValue = profile.ReportsConversions
                ? Math.Round(conversions * rates.ValuePerConversion, 2)
                : null,
        };
    }

    /// <summary>
    /// Decides whether a requested range is the current reporting period or the comparison
    /// period. The comparison period always ends before the current period starts, so testing the
    /// end date against yesterday separates them for either value of "exclude today".
    /// </summary>
    private bool IsCurrentPeriod(ReportScope scope)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.Date);
        return scope.Range.End >= today.AddDays(-1);
    }
}
