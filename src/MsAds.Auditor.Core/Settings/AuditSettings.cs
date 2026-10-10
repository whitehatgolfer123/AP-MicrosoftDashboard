namespace MsAds.Auditor.Core.Settings;

/// <summary>
/// The account-level thresholds the recommendation rules read. Defaults are deliberately
/// conservative investigation thresholds rather than assumed commercial targets: where
/// <see cref="TargetCpa"/> and <see cref="TargetRoas"/> are unset, rules that need a target are
/// suppressed in favour of descriptive observations, because the application does not know what
/// the client is optimising for.
/// </summary>
public sealed record AuditSettings
{
    /// <summary>
    /// The client's target cost per acquisition. Unset means no CPA target is known, and CPA will
    /// be described rather than judged.
    /// </summary>
    public decimal? TargetCpa { get; init; }

    /// <summary>The client's target return on ad spend, as a ratio. Unset means no ROAS target is known.</summary>
    public decimal? TargetRoas { get; init; }

    /// <summary>
    /// Minimum spend before an entity is considered to carry enough evidence to flag. Guards
    /// against calling a campaign bad on the strength of a few pounds.
    /// </summary>
    public decimal MinimumSpend { get; init; } = 50m;

    /// <summary>Minimum clicks before conversion-rate conclusions are drawn.</summary>
    public int MinimumClicks { get; init; } = 20;

    /// <summary>
    /// Conversions needed before an entity is treated as having a reliable conversion signal,
    /// used when deciding whether good performance is worth scaling.
    /// </summary>
    public decimal MinimumConversions { get; init; } = 5m;

    /// <summary>How far above target CPA counts as a problem, as a percentage of the target.</summary>
    public decimal CpaToleranceAboveTargetPercent { get; init; } = 20m;

    /// <summary>Percentage movement treated as a significant period-on-period change.</summary>
    public decimal SignificantChangePercent { get; init; } = 20m;

    /// <summary>Percentage CTR decline treated as worth investigating.</summary>
    public decimal CtrDeclinePercent { get; init; } = 20m;

    /// <summary>Minimum spend before a search term is flagged as potential waste.</summary>
    public decimal SearchTermMinimumSpend { get; init; } = 20m;

    /// <summary>Minimum clicks before a search term's conversion performance is judged.</summary>
    public int SearchTermMinimumClicks { get; init; } = 10;

    /// <summary>ISO currency code used for formatting. Overridden by the account's own currency when known.</summary>
    public string CurrencyCode { get; init; } = "GBP";

    public string CultureName { get; init; } = "en-GB";

    /// <summary>
    /// Impressions needed in both periods before a CTR movement is treated as a signal rather
    /// than noise.
    /// </summary>
    public int MinimumImpressionsForCtr { get; init; } = 500;

    /// <summary>Impression share lost to budget, as a percentage, above which a campaign is flagged as budget constrained.</summary>
    public decimal ImpressionShareLostToBudgetPercent { get; init; } = 20m;

    /// <summary>Percentage fall in impressions treated as a significant delivery change.</summary>
    public decimal DeliveryDropPercent { get; init; } = 70m;

    /// <summary>How many times the account average CPA a campaign must reach before being flagged as an outlier.</summary>
    public decimal CpaVersusAccountMultiple { get; init; } = 1.5m;

    /// <summary>Days either side of a recorded change compared when looking for an association with performance.</summary>
    public int ChangeImpactWindowDays { get; init; } = 3;

    public bool HasTargetCpa => TargetCpa is > 0m;

    public bool HasTargetRoas => TargetRoas is > 0m;

    /// <summary>The CPA above which an entity is flagged, or null when no target is configured.</summary>
    public decimal? CpaFlagThreshold => HasTargetCpa
        ? TargetCpa!.Value * (1m + CpaToleranceAboveTargetPercent / 100m)
        : null;

    /// <summary>Validates the settings, returning messages for any values that cannot be used.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (TargetCpa is <= 0m) errors.Add("Target CPA must be greater than zero when set.");
        if (TargetRoas is <= 0m) errors.Add("Target ROAS must be greater than zero when set.");
        if (MinimumSpend < 0m) errors.Add("Minimum spend cannot be negative.");
        if (MinimumClicks < 0) errors.Add("Minimum clicks cannot be negative.");
        if (MinimumConversions < 0m) errors.Add("Minimum conversions cannot be negative.");
        if (SignificantChangePercent <= 0m) errors.Add("Significant change percentage must be greater than zero.");
        if (CtrDeclinePercent <= 0m) errors.Add("CTR decline percentage must be greater than zero.");
        if (SearchTermMinimumSpend < 0m) errors.Add("Search term minimum spend cannot be negative.");
        if (SearchTermMinimumClicks < 0) errors.Add("Search term minimum clicks cannot be negative.");

        return errors;
    }
}
