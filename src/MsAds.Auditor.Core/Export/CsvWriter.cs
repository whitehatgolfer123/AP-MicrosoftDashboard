using System.Buffers;
using System.Globalization;
using System.Text;

namespace MsAds.Auditor.Core.Export;

/// <summary>
/// Builds RFC 4180 compliant CSV. Fields containing a comma, quote or newline are quoted and
/// embedded quotes doubled, so search terms and campaign names cannot break the file.
/// </summary>
public sealed class CsvWriter
{
    /// <summary>Characters that force a field to be quoted.</summary>
    private static readonly SearchValues<char> MustQuote = SearchValues.Create(",\"\n\r");

    private readonly StringBuilder _builder = new();
    private int _columnCount;

    /// <summary>Writes the header row and fixes the column count that later rows must match.</summary>
    public CsvWriter Header(params string[] columns)
    {
        _columnCount = columns.Length;
        return Row(columns);
    }

    public CsvWriter Row(params string?[] values)
    {
        if (_columnCount > 0 && values.Length != _columnCount)
        {
            throw new InvalidOperationException(
                $"Row has {values.Length} value(s) but the header declared {_columnCount} column(s).");
        }

        _builder.AppendLine(string.Join(',', values.Select(Escape)));
        return this;
    }

    /// <summary>
    /// A comment-style preamble line. Written as a single quoted field so that spreadsheet
    /// software treats it as text rather than as data columns.
    /// </summary>
    public CsvWriter Preamble(string text)
    {
        _builder.AppendLine(Escape(text));
        return this;
    }

    public CsvWriter Blank()
    {
        _builder.AppendLine();
        return this;
    }

    public string Build() => _builder.ToString();

    public override string ToString() => Build();

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuoting = value.AsSpan().IndexOfAny(MustQuote) >= 0
            || value.StartsWith(' ') || value.EndsWith(' ');

        return needsQuoting ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }

    /// <summary>
    /// Formats a number for export: invariant culture, no thousands separators and no currency
    /// symbol, so the file imports cleanly. Missing values are written as an empty field, which
    /// keeps "no data" distinguishable from zero.
    /// </summary>
    public static string Number(decimal? value, int decimals = 2)
        => value?.ToString("F" + decimals, CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string DateTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
