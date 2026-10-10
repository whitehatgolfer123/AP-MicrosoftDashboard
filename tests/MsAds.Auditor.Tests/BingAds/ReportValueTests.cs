using MsAds.Auditor.BingAds.Reporting;

namespace MsAds.Auditor.Tests.BingAds;

/// <summary>
/// Parsing of Microsoft Advertising report cells. The platform writes "--" for an absent value
/// and may decorate numbers, so every cell goes through this layer rather than being read
/// directly.
/// </summary>
public class ReportValueTests
{
    /// <summary>A report row built from a dictionary, standing in for a downloaded row.</summary>
    private sealed class FakeRow(params (string Column, string? Value)[] cells) : IReportRow
    {
        private readonly Dictionary<string, string?> _cells =
            cells.ToDictionary(c => c.Column, c => c.Value, StringComparer.OrdinalIgnoreCase);

        public string? Raw(string column) => _cells.GetValueOrDefault(column);
    }

    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("12.34 %", 12.34)]
    [InlineData("12.34%", 12.34)]
    [InlineData("-5.5", -5.5)]
    [InlineData("0", 0)]
    public void Numbers_parse_through_the_decoration_the_platform_adds(string raw, double expected)
        => Assert.Equal((decimal)expected, ReportValue.Number(new FakeRow(("Spend", raw)), "Spend"));

    [Theory]
    [InlineData("--")]
    [InlineData("")]
    [InlineData("n/a")]
    public void The_platforms_null_markers_parse_as_no_value(string raw)
    {
        Assert.Null(ReportValue.Number(new FakeRow(("Spend", raw)), "Spend"));
        Assert.Null(ReportValue.Text(new FakeRow(("Spend", raw)), "Spend"));
    }

    [Fact]
    public void A_column_the_report_did_not_return_parses_as_no_value()
        => Assert.Null(ReportValue.Number(new FakeRow(), "Spend"));

    [Fact]
    public void Unparseable_text_becomes_no_value_rather_than_zero()
        => Assert.Null(ReportValue.Number(new FakeRow(("Spend", "not a number")), "Spend"));

    [Fact]
    public void Money_defaults_to_zero_because_cost_columns_are_always_returned()
        => Assert.Equal(0m, ReportValue.Money(new FakeRow(("Spend", "--")), "Spend"));

    [Fact]
    public void Integers_round_rather_than_truncate()
        => Assert.Equal(13L, ReportValue.Integer(new FakeRow(("Clicks", "12.6")), "Clicks"));

    [Theory]
    [InlineData("2026-03-15")]
    [InlineData("3/15/2026")]
    public void Dates_parse_in_the_formats_the_reports_use(string raw)
        => Assert.Equal(new DateOnly(2026, 3, 15), ReportValue.Date(new FakeRow(("TimePeriod", raw)), "TimePeriod"));

    [Fact]
    public void An_unparseable_date_is_null_so_the_row_can_be_skipped()
        => Assert.Null(ReportValue.Date(new FakeRow(("TimePeriod", "whenever")), "TimePeriod"));

    [Fact]
    public void Conversions_prefer_the_fractional_column_so_part_conversions_are_not_lost()
    {
        var row = new FakeRow(("Conversions", "12"), ("ConversionsQualified", "12.75"));

        Assert.Equal(12.75m, ReportValue.Conversions(row));
    }

    [Fact]
    public void Conversions_fall_back_to_the_whole_number_column()
        => Assert.Equal(12m, ReportValue.Conversions(new FakeRow(("Conversions", "12"))));

    [Fact]
    public void Conversions_are_null_when_neither_column_was_returned()
    {
        // The distinction the whole application rests on: unavailable is not zero.
        Assert.Null(ReportValue.Conversions(new FakeRow(("Clicks", "100"))));
    }

    [Fact]
    public void A_recorded_zero_conversion_count_is_kept_as_zero()
        => Assert.Equal(0m, ReportValue.Conversions(new FakeRow(("ConversionsQualified", "0"))));
}
