using MsAds.Auditor.Core.Export;

namespace MsAds.Auditor.Tests.Export;

public class CsvWriterTests
{
    [Fact]
    public void Plain_values_are_written_unquoted()
    {
        var csv = new CsvWriter().Header("a", "b").Row("1", "2").Build();

        Assert.Equal("a,b" + Environment.NewLine + "1,2" + Environment.NewLine, csv);
    }

    [Theory]
    [InlineData("boiler, cheap", "\"boiler, cheap\"")]
    [InlineData("say \"hello\"", "\"say \"\"hello\"\"\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData(" padded ", "\" padded \"")]
    public void Values_that_could_break_the_file_are_quoted_and_escaped(string input, string expected)
    {
        var csv = new CsvWriter().Row(input).Build();

        Assert.Equal(expected + Environment.NewLine, csv);
    }

    [Fact]
    public void Null_and_empty_values_become_empty_fields()
        => Assert.Equal("," + Environment.NewLine, new CsvWriter().Row(null, "").Build());

    [Fact]
    public void A_row_with_the_wrong_number_of_columns_is_rejected_rather_than_silently_misaligned()
    {
        var writer = new CsvWriter().Header("a", "b");

        Assert.Throws<InvalidOperationException>(() => writer.Row("only one"));
    }

    [Fact]
    public void Numbers_are_written_in_invariant_culture_without_separators_so_they_import_cleanly()
    {
        Assert.Equal("1234.50", CsvWriter.Number(1234.5m));
        Assert.Equal("1234.5", CsvWriter.Number(1234.5m, 1));
        Assert.Equal("42", CsvWriter.Number(42L));
    }

    [Fact]
    public void A_missing_number_is_an_empty_field_so_no_data_stays_distinct_from_zero()
    {
        Assert.Equal("", CsvWriter.Number(null));
        Assert.Equal("0.00", CsvWriter.Number(0m));
    }

    [Fact]
    public void Dates_are_written_in_sortable_iso_form()
        => Assert.Equal("2026-03-15", CsvWriter.Date(new DateOnly(2026, 3, 15)));
}
