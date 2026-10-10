using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.Reports;

/// <summary>
/// Orchestrates one audit: retrieves the requested datasets for both periods, aligns them,
/// runs the analyses, and assembles the report.
/// </summary>
public sealed class AuditReportBuilder(
    RecommendationEngine engine,
    SearchTermWasteAnalyzer searchTermAnalyzer,
    ExecutiveSummaryGenerator summaryGenerator,
    IClock clock,
    ILogger<AuditReportBuilder> logger)
{
    public async Task<AuditReport> BuildAsync(
        IAdsDataSource source,
        AdAccountInfo account,
        ResolvedPeriod period,
        AuditSettings settings,
        AnalysisSelection selection,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // The account's own currency wins over the configured default, so figures are never
        // presented in a currency the account does not report in.
        var formatter = new MetricFormatter(settings.CultureName, account.CurrencyCode ?? settings.CurrencyCode);

        var currentScope = ReportScope.For(account, period.Current);
        var previousScope = period.Previous is { } previousRange ? currentScope.ForRange(previousRange) : null;

        logger.LogInformation(
            "Building audit for account {AccountId} over {Current} (comparison: {Previous}) from {Source}",
            account.AccountId, period.Current, period.Previous?.ToString() ?? "none", source.DisplayName);

        // Daily totals drive both the charts and the account-level figures, so they are always fetched.
        var dailyCurrentTask = source.GetDailyMetricsAsync(currentScope, cancellationToken);
        var dailyPreviousTask = previousScope is null
            ? Task.FromResult(new DataSet<DailyMetrics>())
            : source.GetDailyMetricsAsync(previousScope, cancellationToken);

        var campaignsCurrentTask = selection.Campaigns
            ? source.GetCampaignPerformanceAsync(currentScope, cancellationToken)
            : Task.FromResult(new DataSet<CampaignPerformance>());
        var campaignsPreviousTask = selection.Campaigns && previousScope is not null
            ? source.GetCampaignPerformanceAsync(previousScope, cancellationToken)
            : Task.FromResult(new DataSet<CampaignPerformance>());

        var keywordsCurrentTask = selection.Keywords
            ? source.GetKeywordPerformanceAsync(currentScope, cancellationToken)
            : Task.FromResult(new DataSet<KeywordPerformance>());
        var keywordsPreviousTask = selection.Keywords && previousScope is not null
            ? source.GetKeywordPerformanceAsync(previousScope, cancellationToken)
            : Task.FromResult(new DataSet<KeywordPerformance>());

        var searchTermsTask = selection.SearchTerms
            ? source.GetSearchTermsAsync(currentScope, cancellationToken)
            : Task.FromResult(new DataSet<SearchTermPerformance>());

        var changesTask = selection.ChangeHistory
            ? source.GetChangeHistoryAsync(currentScope, cancellationToken)
            : Task.FromResult(new DataSet<ChangeRecord>());

        var conflictsTask = selection.NegativeKeywordConflicts
            ? source.GetNegativeKeywordConflictsAsync(currentScope, cancellationToken)
            : Task.FromResult(new DataSet<NegativeKeywordConflict>());

        await Task.WhenAll(
            dailyCurrentTask, dailyPreviousTask,
            campaignsCurrentTask, campaignsPreviousTask,
            keywordsCurrentTask, keywordsPreviousTask,
            searchTermsTask, changesTask, conflictsTask).ConfigureAwait(false);

        var dailyCurrent = await dailyCurrentTask.ConfigureAwait(false);
        var dailyPrevious = await dailyPreviousTask.ConfigureAwait(false);
        var campaignsCurrent = await campaignsCurrentTask.ConfigureAwait(false);
        var campaignsPrevious = await campaignsPreviousTask.ConfigureAwait(false);
        var keywordsCurrent = await keywordsCurrentTask.ConfigureAwait(false);
        var keywordsPrevious = await keywordsPreviousTask.ConfigureAwait(false);
        var searchTerms = await searchTermsTask.ConfigureAwait(false);
        var changes = await changesTask.ConfigureAwait(false);
        var conflicts = await conflictsTask.ConfigureAwait(false);

        var current = MetricSet.Sum(dailyCurrent.Rows.Select(d => d.Metrics));
        var previous = previousScope is null || dailyPrevious.IsEmpty
            ? null
            : MetricSet.Sum(dailyPrevious.Rows.Select(d => d.Metrics));

        // Conversion data is unavailable only when nothing at all reported it. A single row with a
        // value means the column exists, and a zero elsewhere is then a genuine zero.
        var conversionDataUnavailable =
            !dailyCurrent.Rows.Any(d => d.Metrics.HasConversionData)
            && !campaignsCurrent.Rows.Any(c => c.Metrics.HasConversionData);

        var campaigns = AlignCampaigns(campaignsCurrent.Rows, campaignsPrevious.Rows);
        var keywords = AlignKeywords(keywordsCurrent.Rows, keywordsPrevious.Rows);

        var context = new AuditContext
        {
            Scope = currentScope,
            Period = period,
            Settings = settings,
            Formatter = formatter,
            AccountCurrent = current,
            AccountPrevious = previous,
            Campaigns = campaigns,
            Keywords = keywords,
            SearchTerms = searchTerms.Rows,
            Changes = changes.Rows,
            NegativeKeywordConflicts = conflicts.Rows,
            DailyCurrent = dailyCurrent.Rows,
            DailyPrevious = dailyPrevious.Rows,
            ConversionDataUnavailable = conversionDataUnavailable,
            SearchTermDataUnavailable = searchTerms.IsUnavailable,
            ChangeHistoryUnavailable = changes.IsUnavailable,
        };

        var searchTermAnalysis = selection.SearchTerms
            ? searchTermAnalyzer.Analyse(searchTerms, settings, current, formatter, !conversionDataUnavailable)
            : new SearchTermAnalysis();

        var recommendations = engine.Evaluate(context);
        var summary = summaryGenerator.Generate(context, recommendations, searchTermAnalysis);

        var notices = CollectNotices(
            period, current, conversionDataUnavailable, source,
            [dailyCurrent.Notices, dailyPrevious.Notices, campaignsCurrent.Notices, campaignsPrevious.Notices,
             keywordsCurrent.Notices, keywordsPrevious.Notices, searchTerms.Notices, changes.Notices, conflicts.Notices]);

        if (recommendations.RuleFailures.Count > 0)
        {
            foreach (var failure in recommendations.RuleFailures)
            {
                logger.LogWarning("Audit rule failed to evaluate: {Failure}", failure);
            }
        }

        stopwatch.Stop();

        return new AuditReport
        {
            ReportId = Guid.NewGuid(),
            GeneratedAt = clock.UtcNow,
            Account = account,
            Period = period,
            Settings = settings,
            Selection = selection,
            SourceKind = source.Kind,
            SourceName = source.DisplayName,
            Current = current,
            Previous = previous,
            Summary = MetricSet.Compare(current, previous),
            DailyCurrent = dailyCurrent.Rows,
            DailyPrevious = dailyPrevious.Rows,
            Campaigns = campaigns,
            Keywords = keywords,
            SearchTerms = searchTermAnalysis,
            Changes = [.. changes.Rows.OrderByDescending(c => c.ChangedAt)],
            NegativeKeywordConflicts = conflicts.Rows,
            Recommendations = recommendations,
            ExecutiveSummary = summary,
            Notices = notices,
            Formatter = formatter,
            ConversionDataUnavailable = conversionDataUnavailable,
            ChangeHistoryUnavailable = changes.IsUnavailable,
            Duration = stopwatch.Elapsed,
        };
    }

    /// <summary>
    /// Pairs each campaign's current and previous rows by id, keeping campaigns that appear in
    /// only one period so that newly started and newly stopped campaigns are both visible.
    /// </summary>
    internal static IReadOnlyList<ComparedEntity> AlignCampaigns(
        IReadOnlyList<CampaignPerformance> current, IReadOnlyList<CampaignPerformance> previous)
    {
        var previousById = previous
            .GroupBy(c => c.CampaignId)
            .ToDictionary(g => g.Key, g => g.First());

        var compared = current.Select(c => new ComparedEntity
        {
            EntityType = "Campaign",
            EntityId = c.CampaignId,
            Name = c.CampaignName,
            Status = c.Status,
            Context = c.CampaignType,
            Current = c.Metrics,
            Previous = previousById.TryGetValue(c.CampaignId, out var p) ? p.Metrics : null,
            ImpressionShare = c.ImpressionShare,
            ImpressionShareLostToBudget = c.ImpressionShareLostToBudget,
            ImpressionShareLostToRank = c.ImpressionShareLostToRank,
        }).ToList();

        // Campaigns present previously but absent now have stopped entirely; keep them visible.
        var currentIds = current.Select(c => c.CampaignId).ToHashSet();

        compared.AddRange(previous
            .Where(p => !currentIds.Contains(p.CampaignId))
            .Select(p => new ComparedEntity
            {
                EntityType = "Campaign",
                EntityId = p.CampaignId,
                Name = p.CampaignName,
                Status = p.Status,
                Context = p.CampaignType,
                Current = MetricSet.Empty with { Conversions = p.Metrics.HasConversionData ? 0m : null },
                Previous = p.Metrics,
            }));

        return [.. compared.OrderByDescending(c => c.Current.Spend)];
    }

    internal static IReadOnlyList<ComparedEntity> AlignKeywords(
        IReadOnlyList<KeywordPerformance> current, IReadOnlyList<KeywordPerformance> previous)
    {
        var previousById = previous
            .GroupBy(k => k.KeywordId)
            .ToDictionary(g => g.Key, g => g.First());

        return
        [
            .. current.Select(k => new ComparedEntity
            {
                EntityType = "Keyword",
                EntityId = k.KeywordId,
                Name = k.MatchType is null ? k.Keyword : $"{k.Keyword} [{k.MatchType}]",
                Status = k.Status,
                Context = k.CampaignName is null ? k.AdGroupName : $"{k.CampaignName} › {k.AdGroupName}",
                Current = k.Metrics,
                Previous = previousById.TryGetValue(k.KeywordId, out var p) ? p.Metrics : null,
                QualityScore = k.QualityScore,
            }).OrderByDescending(k => k.Current.Spend),
        ];
    }

    /// <summary>
    /// Gathers notices from every dataset, removes duplicates, and adds the report-level ones the
    /// user needs regardless of data source.
    /// </summary>
    private static IReadOnlyList<DataNotice> CollectNotices(
        ResolvedPeriod period,
        MetricSet current,
        bool conversionDataUnavailable,
        IAdsDataSource source,
        IReadOnlyList<IReadOnlyList<DataNotice>> noticeSets)
    {
        var notices = new List<DataNotice>();

        if (source.Kind == DataSourceKind.Demo)
        {
            notices.Add(DataNotice.Warning(
                DataNotices.DemoDataCode,
                "This report was generated from demo data. The figures are synthetic and must not be presented as "
                + "live advertising performance."));
        }

        if (period.CurrentDayExcluded)
        {
            notices.Add(DataNotice.Info(
                DataNotices.PartialDayExcludedCode,
                $"Today ({period.AccountToday:d MMM yyyy} in the account's {period.TimeZoneId} time zone) is "
                + "excluded from both periods because its data is still accumulating."));
        }

        if (period.TimeZoneFellBackToUtc)
        {
            notices.Add(DataNotice.Warning(
                "timezone-fallback",
                "The account's reporting time zone was not available, so UTC was used to determine the reporting "
                + "dates. Dates may be offset by a day relative to the Microsoft Advertising interface."));
        }

        notices.Add(DataNotices.ReportingLag());

        if (conversionDataUnavailable)
        {
            notices.Add(DataNotices.ConversionDataUnavailable());
        }

        if (current.IsEmpty)
        {
            notices.Add(DataNotices.EmptyPeriod());
        }

        foreach (var set in noticeSets)
        {
            notices.AddRange(set);
        }

        return
        [
            .. notices
                .GroupBy(n => (n.Code, n.Scope))
                .Select(g => g.First())
                .OrderBy(n => n.Severity == NoticeSeverity.Error ? 0 : n.Severity == NoticeSeverity.Warning ? 1 : 2),
        ];
    }
}
