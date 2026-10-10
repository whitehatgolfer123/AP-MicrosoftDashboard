using Microsoft.Extensions.DependencyInjection;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Rules.Implementations;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.DependencyInjection;

public static class AuditorCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the analysis layer: the clock, the calculation and analysis services, and every
    /// audit rule. Rules are registered individually so the set can be inspected in the interface
    /// and overridden in tests.
    /// </summary>
    public static IServiceCollection AddAuditorCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ReportingPeriodResolver>();
        services.AddSingleton<SearchTermWasteAnalyzer>();
        services.AddSingleton<ExecutiveSummaryGenerator>();
        services.AddSingleton<AuditReportBuilder>();
        services.AddSingleton<PortfolioScanner>();

        services.AddAuditRules();
        services.AddSingleton<RecommendationEngine>();

        return services;
    }

    /// <summary>
    /// Registers the rule set. Campaign and keyword variants of the shared rules are registered
    /// separately so each has its own stable rule id.
    /// </summary>
    public static IServiceCollection AddAuditRules(this IServiceCollection services)
    {
        // Account level.
        services.AddSingleton<IAuditRule, AccountEfficiencyRule>();
        services.AddSingleton<IAuditRule, ConversionTrackingRule>();

        // Campaign level.
        services.AddSingleton<IAuditRule>(_ => new SpendWithNoConversionsRule(EntityScope.Campaign));
        services.AddSingleton<IAuditRule>(_ => new CpaAboveTargetRule(EntityScope.Campaign));
        services.AddSingleton<IAuditRule>(_ => new SpendUpConversionsDownRule(EntityScope.Campaign));
        services.AddSingleton<IAuditRule>(_ => new CtrDeclineRule(EntityScope.Campaign));
        services.AddSingleton<IAuditRule>(_ => new DeliveryChangeRule(EntityScope.Campaign));
        services.AddSingleton<IAuditRule>(_ => new StrongPerformerRule(EntityScope.Campaign));
        services.AddSingleton<IAuditRule, ImpressionShareLostToBudgetRule>();
        services.AddSingleton<IAuditRule, CampaignVersusAccountAverageRule>();

        // Keyword level.
        services.AddSingleton<IAuditRule>(_ => new SpendWithNoConversionsRule(EntityScope.Keyword));
        services.AddSingleton<IAuditRule>(_ => new CpaAboveTargetRule(EntityScope.Keyword));
        services.AddSingleton<IAuditRule>(_ => new CtrDeclineRule(EntityScope.Keyword));
        services.AddSingleton<IAuditRule>(_ => new CpcIncreaseRule(EntityScope.Keyword));
        services.AddSingleton<IAuditRule>(_ => new StrongPerformerRule(EntityScope.Keyword));

        // Changes and conflicts.
        services.AddSingleton<IAuditRule, ChangeCorrelationRule>();
        services.AddSingleton<IAuditRule, NegativeKeywordConflictRule>();

        return services;
    }
}
