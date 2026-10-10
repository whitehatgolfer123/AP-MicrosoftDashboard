namespace MsAds.Auditor.Core.Metrics;

/// <summary>
/// The raw, retrieved counters for one entity over one period, plus the derived metrics
/// calculated from them.
/// </summary>
/// <remarks>
/// Counters that Microsoft Advertising always returns are non-nullable. Conversion columns are
/// nullable because they are genuinely optional: <see langword="null"/> means the data was not
/// available (column not requested, not returned, or conversion tracking not configured), which
/// is a different statement from a recorded zero. Derived metrics are <see langword="null"/>
/// whenever their denominator is zero or an input is missing.
/// </remarks>
public sealed record MetricSet
{
    public static readonly MetricSet Empty = new();

    public long Impressions { get; init; }

    public long Clicks { get; init; }

    /// <summary>Cost in the account's reporting currency.</summary>
    public decimal Spend { get; init; }

    /// <summary>
    /// Conversions attributed by the account's selected conversion goals and attribution model.
    /// <see langword="null"/> when conversion data is unavailable rather than zero.
    /// </summary>
    public decimal? Conversions { get; init; }

    /// <summary>Revenue attributed to conversions, where the account records conversion value.</summary>
    public decimal? ConversionValue { get; init; }

    /// <summary>True when conversion data was actually returned, as opposed to being absent.</summary>
    public bool HasConversionData => Conversions.HasValue;

    public bool HasConversionValueData => ConversionValue.HasValue;

    /// <summary>True when nothing at all was delivered in the period.</summary>
    public bool IsEmpty => Impressions == 0 && Clicks == 0 && Spend == 0m;

    /// <summary>Click-through rate as a percentage. Null when there were no impressions.</summary>
    public decimal? ClickThroughRate => MetricMath.Rate(Clicks, Impressions);

    /// <summary>Average cost per click. Null when there were no clicks.</summary>
    public decimal? AverageCpc => MetricMath.Divide(Spend, Clicks);

    /// <summary>Conversion rate as a percentage. Null when there were no clicks or no conversion data.</summary>
    public decimal? ConversionRate => MetricMath.Rate(Conversions, Clicks);

    /// <summary>
    /// Cost per acquisition. Null when there were no conversions — spend with zero conversions has
    /// no finite CPA, and must not be rendered as an enormous number.
    /// </summary>
    public decimal? CostPerAcquisition => MetricMath.Divide(Spend, Conversions);

    /// <summary>Return on ad spend as a ratio (2.5 means £2.50 back per £1 spent).</summary>
    public decimal? ReturnOnAdSpend => MetricMath.Divide(ConversionValue, Spend);

    /// <summary>Average value per conversion.</summary>
    public decimal? ValuePerConversion => MetricMath.Divide(ConversionValue, Conversions);

    /// <summary>
    /// True when the entity took clicks and spent money but recorded no conversions. Distinct from
    /// having no conversion data at all.
    /// </summary>
    public bool HasSpendButNoConversions
        => Spend > 0m && HasConversionData && Conversions!.Value == 0m;

    public decimal? Get(MetricKey key) => key switch
    {
        MetricKey.Spend => Spend,
        MetricKey.Impressions => Impressions,
        MetricKey.Clicks => Clicks,
        MetricKey.ClickThroughRate => ClickThroughRate,
        MetricKey.AverageCpc => AverageCpc,
        MetricKey.Conversions => Conversions,
        MetricKey.ConversionRate => ConversionRate,
        MetricKey.CostPerAcquisition => CostPerAcquisition,
        MetricKey.ConversionValue => ConversionValue,
        MetricKey.ReturnOnAdSpend => ReturnOnAdSpend,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown metric."),
    };

    /// <summary>
    /// Totals a collection of metric sets. Conversion columns stay <see langword="null"/> unless
    /// at least one input carried a value, so an unavailable column is never reported as zero.
    /// </summary>
    public static MetricSet Sum(IEnumerable<MetricSet> sets)
    {
        var materialised = sets as ICollection<MetricSet> ?? [.. sets];

        return new MetricSet
        {
            Impressions = materialised.Sum(s => s.Impressions),
            Clicks = materialised.Sum(s => s.Clicks),
            Spend = materialised.Sum(s => s.Spend),
            Conversions = MetricMath.SumOptional(materialised.Select(s => s.Conversions)),
            ConversionValue = MetricMath.SumOptional(materialised.Select(s => s.ConversionValue)),
        };
    }

    /// <summary>Builds the full set of period-on-period comparisons for the dashboard cards.</summary>
    public static IReadOnlyList<MetricComparison> Compare(MetricSet current, MetricSet? previous)
        => [.. MetricCatalogue.DashboardOrder.Select(key => new MetricComparison(
            key,
            MetricDelta.Create(current.Get(key), previous?.Get(key), MetricCatalogue.Describe(key).Polarity)))];
}

/// <summary>A metric paired with how it moved, ready for display.</summary>
public sealed record MetricComparison(MetricKey Key, MetricDelta Delta)
{
    public MetricDescriptor Descriptor => MetricCatalogue.Describe(Key);
}
