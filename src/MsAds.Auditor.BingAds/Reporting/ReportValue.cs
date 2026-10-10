using System.Globalization;

namespace MsAds.Auditor.BingAds.Reporting;

/// <summary>
/// A single row of a downloaded report, addressed by column name. Abstracted from the SDK's own
/// record type so that the mapping from report columns to domain models can be unit tested
/// against fabricated rows, with no API access.
/// </summary>
public interface IReportRow
{
    /// <summary>The raw cell value, or <see langword="null"/> when the column is absent.</summary>
    string? Raw(string column);
}

/// <summary>
/// Tolerant parsing of Microsoft Advertising report cells.
/// </summary>
/// <remarks>
/// Reports represent an absent value as <c>--</c>, and depending on the format version may
/// decorate numbers with a percent sign, thousands separators or a currency symbol. Every value
/// read from a report goes through here, and anything that cannot be understood becomes
/// <see langword="null"/> rather than a silent zero — the distinction the rest of the
/// application depends on.
/// </remarks>
public static class ReportValue
{
    /// <summary>The placeholders Microsoft Advertising uses for "no value".</summary>
    private static readonly string[] NullMarkers = ["--", "", "n/a", "N/A", "null"];

    /// <summary>Returns the trimmed text, or <see langword="null"/> when it means "no value".</summary>
    public static string? Text(IReportRow row, string column)
    {
        var raw = row.Raw(column)?.Trim();

        return raw is null || NullMarkers.Contains(raw, StringComparer.OrdinalIgnoreCase)
            ? null
            : raw;
    }

    /// <summary>
    /// Parses a numeric cell, tolerating a percent sign, a currency symbol, thousands separators
    /// and surrounding whitespace. Returns <see langword="null"/> when the cell is absent, is a
    /// null marker, or cannot be parsed as a number.
    /// </summary>
    public static decimal? Number(IReportRow row, string column)
    {
        var text = Text(row, column);

        if (text is null)
        {
            return null;
        }

        // Strip decoration the report may include, keeping the sign and decimal point.
        Span<char> buffer = stackalloc char[text.Length];
        var length = 0;

        foreach (var ch in text)
        {
            if (char.IsAsciiDigit(ch) || ch is '.' or '-' or '+')
            {
                buffer[length++] = ch;
            }
            else if (ch is ',')
            {
                // A comma is a thousands separator in these reports; the API emits '.' decimals.
                continue;
            }
        }

        var cleaned = new string(buffer[..length]);

        return decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>A whole-number cell, rounded if the report supplied decimals.</summary>
    public static long Integer(IReportRow row, string column)
        => Number(row, column) is { } value ? (long)Math.Round(value, MidpointRounding.AwayFromZero) : 0L;

    /// <summary>A money cell. Zero when absent, because cost columns are always returned.</summary>
    public static decimal Money(IReportRow row, string column) => Number(row, column) ?? 0m;

    /// <summary>
    /// A date cell. Report dates are ISO formatted under format version 2.0; other formats are
    /// accepted as a fallback.
    /// </summary>
    public static DateOnly? Date(IReportRow row, string column)
    {
        var text = Text(row, column);

        if (text is null)
        {
            return null;
        }

        string[] formats = ["yyyy-MM-dd", "M/d/yyyy", "d/M/yyyy", "yyyy/MM/dd", "MM/dd/yyyy"];

        if (DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose)
            ? loose
            : null;
    }

    /// <summary>A date and time cell, as used by the change history report.</summary>
    public static DateTimeOffset? Timestamp(IReportRow row, string column)
    {
        var text = Text(row, column);

        if (text is null)
        {
            return null;
        }

        // The report expresses times in the account's reporting time zone with no offset, so the
        // value is read as unspecified and presented as-is rather than being shifted.
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.Zero);
        }

        return null;
    }

    /// <summary>
    /// Reads the conversion count, preferring the fractional <c>ConversionsQualified</c> column
    /// where the account provides it, and falling back to the whole-number <c>Conversions</c>
    /// column. Returns <see langword="null"/> when neither is present, so that unavailable
    /// conversion data stays distinguishable from a recorded zero.
    /// </summary>
    public static decimal? Conversions(IReportRow row)
        => Number(row, "ConversionsQualified") ?? Number(row, "Conversions");
}
