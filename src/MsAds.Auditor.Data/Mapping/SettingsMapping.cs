using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Data.Entities;

namespace MsAds.Auditor.Data.Mapping;

/// <summary>Translates the analysis thresholds between the domain record and its single row.</summary>
internal static class SettingsMapping
{
    /// <summary>
    /// Copies the settings onto an existing row, so the singleton is updated rather than
    /// replaced and <c>UpdatedAt</c> reflects the save.
    /// </summary>
    internal static void CopyOnto(
        StoredSettingsEntity entity,
        AuditSettings settings,
        DateTimeOffset at,
        string? by)
    {
        entity.Id = StoredSettingsEntity.SingletonId;
        entity.UpdatedAt = at;
        entity.UpdatedBy = by;

        entity.TargetCpa = settings.TargetCpa;
        entity.TargetRoas = settings.TargetRoas;
        entity.MinimumSpend = settings.MinimumSpend;
        entity.MinimumClicks = settings.MinimumClicks;
        entity.MinimumConversions = settings.MinimumConversions;
        entity.CpaToleranceAboveTargetPercent = settings.CpaToleranceAboveTargetPercent;
        entity.SignificantChangePercent = settings.SignificantChangePercent;
        entity.CtrDeclinePercent = settings.CtrDeclinePercent;
        entity.SearchTermMinimumSpend = settings.SearchTermMinimumSpend;
        entity.SearchTermMinimumClicks = settings.SearchTermMinimumClicks;
        entity.CurrencyCode = settings.CurrencyCode;
        entity.CultureName = settings.CultureName;
        entity.MinimumImpressionsForCtr = settings.MinimumImpressionsForCtr;
        entity.ImpressionShareLostToBudgetPercent = settings.ImpressionShareLostToBudgetPercent;
        entity.DeliveryDropPercent = settings.DeliveryDropPercent;
        entity.CpaVersusAccountMultiple = settings.CpaVersusAccountMultiple;
        entity.ChangeImpactWindowDays = settings.ChangeImpactWindowDays;
    }

    internal static AuditSettings ToDomain(StoredSettingsEntity entity) => new()
    {
        TargetCpa = entity.TargetCpa,
        TargetRoas = entity.TargetRoas,
        MinimumSpend = entity.MinimumSpend,
        MinimumClicks = entity.MinimumClicks,
        MinimumConversions = entity.MinimumConversions,
        CpaToleranceAboveTargetPercent = entity.CpaToleranceAboveTargetPercent,
        SignificantChangePercent = entity.SignificantChangePercent,
        CtrDeclinePercent = entity.CtrDeclinePercent,
        SearchTermMinimumSpend = entity.SearchTermMinimumSpend,
        SearchTermMinimumClicks = entity.SearchTermMinimumClicks,
        CurrencyCode = entity.CurrencyCode,
        CultureName = entity.CultureName,
        MinimumImpressionsForCtr = entity.MinimumImpressionsForCtr,
        ImpressionShareLostToBudgetPercent = entity.ImpressionShareLostToBudgetPercent,
        DeliveryDropPercent = entity.DeliveryDropPercent,
        CpaVersusAccountMultiple = entity.CpaVersusAccountMultiple,
        ChangeImpactWindowDays = entity.ChangeImpactWindowDays,
    };
}
