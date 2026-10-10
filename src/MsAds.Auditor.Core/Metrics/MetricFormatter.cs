using System.Globalization;

namespace MsAds.Auditor.Core.Metrics;

/// <summary>
/// Renders metric values and changes for display and export, in the account's currency and
/// culture. Missing values render as a dash rather than as zero, so "no data" never reads as
/// "nothing happened".
/// </summary>
public sealed class MetricFormatter
{
    /// <summary>Shown wherever a value is genuinely unavailable.</summary>
    public const string NotAvailable = "—";

    /// <summary>
    /// Separates a textual currency code from the amount. A non-breaking space, written as an
    /// escape so it is visible in the source, so that "PLN 10.00" is never split across lines.
    /// </summary>
    public const string CodeSeparator = "\u00A0";

    private readonly CultureInfo _culture;
    private readonly string _currencySymbol;

    public MetricFormatter(string? cultureName = null, string? currencyCode = null)
    {
        _culture = ResolveCulture(cultureName);
        CurrencyCode = currencyCode ?? _culture.NumberFormat.CurrencySymbol;
        _currencySymbol = CurrencySymbols.TryGetValue(CurrencyCode, out var symbol)
            ? symbol
            : CurrencyCode + CodeSeparator;
    }

    public string CurrencyCode { get; }

    private static readonly Dictionary<string, string> CurrencySymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBP"] = "£",
        ["USD"] = "$",
        ["EUR"] = "€",
        ["AUD"] = "A$",
        ["CAD"] = "C$",
        ["JPY"] = "¥",
        ["INR"] = "₹",
        ["CHF"] = "CHF" + CodeSeparator,
        ["SEK"] = "kr" + CodeSeparator,
        ["NZD"] = "NZ$",
    };

    private static CultureInfo ResolveCulture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>Formats a value according to its metric's conventions.</summary>
    public string Format(decimal? value, MetricFormat format) => value is null
        ? NotAvailable
        : format switch
        {
            MetricFormat.Integer => value.Value == Math.Floor(value.Value)
                ? value.Value.ToString("N0", _culture)
                : value.Value.ToString("N2", _culture),
            MetricFormat.Currency => _currencySymbol + value.Value.ToString("N2", _culture),
            MetricFormat.Percent => value.Value.ToString("N2", _culture) + "%",
            MetricFormat.Ratio => value.Value.ToString("N2", _culture) + "×",
            _ => value.Value.ToString(_culture),
        };

    public string Format(decimal? value, MetricKey key)
        => Format(value, MetricCatalogue.Describe(key).Format);

    public string FormatCurrency(decimal? value) => Format(value, MetricFormat.Currency);

    public string FormatPercent(decimal? value) => Format(value, MetricFormat.Percent);

    public string FormatInteger(decimal? value) => Format(value, MetricFormat.Integer);

    /// <summary>
    /// Describes a change in words the user can trust: a signed percentage where one exists, and
    /// an explicit phrase where a percentage would be misleading.
    /// </summary>
    public string FormatChange(MetricDelta delta) => delta.Kind switch
    {
        ChangeKind.NotComparable => "No comparison",
        ChangeKind.NoChange => "No change",
        ChangeKind.NewActivity => "New activity",
        ChangeKind.CeasedActivity => "Stopped",
        _ => FormatSignedPercent(delta.PercentChange),
    };

    public string FormatSignedPercent(decimal? percent) => percent is null
        ? NotAvailable
        : (percent.Value >= 0 ? "+" : "−") + Math.Abs(percent.Value).ToString("N1", _culture) + "%";

    /// <summary>The absolute change, signed, in the metric's own units.</summary>
    public string FormatAbsoluteChange(MetricDelta delta, MetricKey key)
    {
        if (delta.Absolute is null)
        {
            return NotAvailable;
        }

        var sign = delta.Absolute.Value >= 0 ? "+" : "−";
        return sign + Format(Math.Abs(delta.Absolute.Value), key);
    }

    public string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", _culture);

    public string FormatDateTime(DateTimeOffset value) => value.ToString("d MMM yyyy HH:mm", _culture);
}
