namespace MsAds.Auditor.Core.Metrics;

public enum MetricKey
{
    Spend,
    Impressions,
    Clicks,
    ClickThroughRate,
    AverageCpc,
    Conversions,
    ConversionRate,
    CostPerAcquisition,
    ConversionValue,
    ReturnOnAdSpend,
}

/// <summary>How a metric's value should be rendered.</summary>
public enum MetricFormat
{
    Integer,
    Currency,
    Percent,
    Ratio,
}

/// <summary>
/// Everything the interface needs to know about a metric: its label, how to format it, which
/// direction counts as good, and how it is calculated.
/// </summary>
public sealed record MetricDescriptor(
    MetricKey Key,
    string Name,
    string ShortName,
    MetricFormat Format,
    MetricPolarity Polarity,
    string Definition);

/// <summary>
/// The single source of truth for metric labels, polarity and definitions, so the dashboard,
/// exports and recommendation text cannot drift apart.
/// </summary>
public static class MetricCatalogue
{
    private static readonly Dictionary<MetricKey, MetricDescriptor> Descriptors = new()
    {
        [MetricKey.Spend] = new(
            MetricKey.Spend, "Total spend", "Spend", MetricFormat.Currency, MetricPolarity.Neutral,
            "Cost as reported by Microsoft Advertising, in the account's reporting currency. Rising spend is neither good nor bad on its own — read it alongside conversions and CPA."),

        [MetricKey.Impressions] = new(
            MetricKey.Impressions, "Impressions", "Impr.", MetricFormat.Integer, MetricPolarity.Neutral,
            "Number of times an ad was shown."),

        [MetricKey.Clicks] = new(
            MetricKey.Clicks, "Clicks", "Clicks", MetricFormat.Integer, MetricPolarity.Neutral,
            "Number of clicks recorded by Microsoft Advertising after invalid-click filtering."),

        [MetricKey.ClickThroughRate] = new(
            MetricKey.ClickThroughRate, "Click-through rate", "CTR", MetricFormat.Percent, MetricPolarity.HigherIsBetter,
            "Clicks divided by impressions, as a percentage. Undefined when there were no impressions."),

        [MetricKey.AverageCpc] = new(
            MetricKey.AverageCpc, "Average CPC", "Avg. CPC", MetricFormat.Currency, MetricPolarity.LowerIsBetter,
            "Spend divided by clicks. Undefined when there were no clicks. Treated as lower-is-better, though a rise can reflect deliberate bidding up for more valuable traffic."),

        [MetricKey.Conversions] = new(
            MetricKey.Conversions, "Conversions", "Conv.", MetricFormat.Integer, MetricPolarity.HigherIsBetter,
            "Conversions attributed by the conversion goals included in the account's reporting, under its configured attribution model. A blank value means conversion data was unavailable, not that there were zero."),

        [MetricKey.ConversionRate] = new(
            MetricKey.ConversionRate, "Conversion rate", "Conv. rate", MetricFormat.Percent, MetricPolarity.HigherIsBetter,
            "Conversions divided by clicks, as a percentage. Undefined when there were no clicks."),

        [MetricKey.CostPerAcquisition] = new(
            MetricKey.CostPerAcquisition, "Cost per acquisition", "CPA", MetricFormat.Currency, MetricPolarity.LowerIsBetter,
            "Spend divided by conversions. Undefined when there were no conversions — spend with no conversions has no finite CPA and is reported as a separate finding instead."),

        [MetricKey.ConversionValue] = new(
            MetricKey.ConversionValue, "Conversion value", "Conv. value", MetricFormat.Currency, MetricPolarity.HigherIsBetter,
            "Revenue attributed to conversions. Only available where the account records conversion value."),

        [MetricKey.ReturnOnAdSpend] = new(
            MetricKey.ReturnOnAdSpend, "Return on ad spend", "ROAS", MetricFormat.Ratio, MetricPolarity.HigherIsBetter,
            "Conversion value divided by spend, as a ratio. Undefined when there was no spend or no conversion value data."),
    };

    /// <summary>The order the summary cards appear in, matching the brief's reading order.</summary>
    public static IReadOnlyList<MetricKey> DashboardOrder { get; } =
    [
        MetricKey.Spend,
        MetricKey.Impressions,
        MetricKey.Clicks,
        MetricKey.ClickThroughRate,
        MetricKey.AverageCpc,
        MetricKey.Conversions,
        MetricKey.ConversionRate,
        MetricKey.CostPerAcquisition,
        MetricKey.ConversionValue,
        MetricKey.ReturnOnAdSpend,
    ];

    public static MetricDescriptor Describe(MetricKey key) => Descriptors[key];

    public static IEnumerable<MetricDescriptor> All => DashboardOrder.Select(Describe);
}
