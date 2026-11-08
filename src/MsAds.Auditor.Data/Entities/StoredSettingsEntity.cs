namespace MsAds.Auditor.Data.Entities;

/// <summary>
/// The saved analysis thresholds. One row, keyed by a constant.
/// </summary>
/// <remarks>
/// The values are stored as explicit columns rather than a JSON blob so that a threshold change
/// is visible in a migration and in the table itself. Thresholds are operator preferences, not
/// client data.
/// </remarks>
internal sealed class StoredSettingsEntity
{
    /// <summary>
    /// The only valid key. Settings are application-wide, so the table holds a single row and
    /// the fixed key makes that a constraint rather than a convention.
    /// </summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who last saved them, where sign-in is enabled.</summary>
    public string? UpdatedBy { get; set; }

    public decimal? TargetCpa { get; set; }

    public decimal? TargetRoas { get; set; }

    public decimal MinimumSpend { get; set; }

    public int MinimumClicks { get; set; }

    public decimal MinimumConversions { get; set; }

    public decimal CpaToleranceAboveTargetPercent { get; set; }

    public decimal SignificantChangePercent { get; set; }

    public decimal CtrDeclinePercent { get; set; }

    public decimal SearchTermMinimumSpend { get; set; }

    public int SearchTermMinimumClicks { get; set; }

    public string CurrencyCode { get; set; } = "GBP";

    public string CultureName { get; set; } = "en-GB";

    public int MinimumImpressionsForCtr { get; set; }

    public decimal ImpressionShareLostToBudgetPercent { get; set; }

    public decimal DeliveryDropPercent { get; set; }

    public decimal CpaVersusAccountMultiple { get; set; }

    public int ChangeImpactWindowDays { get; set; }
}
