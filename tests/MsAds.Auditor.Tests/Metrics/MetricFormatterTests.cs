using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Tests.Metrics;

public class MetricFormatterTests
{
    private readonly MetricFormatter _formatter = new("en-GB", "GBP");

    [Fact]
    public void Missing_values_render_as_a_dash_so_no_data_never_reads_as_zero()
    {
        Assert.Equal(MetricFormatter.NotAvailable, _formatter.FormatCurrency(null));
        Assert.Equal(MetricFormatter.NotAvailable, _formatter.FormatPercent(null));
        Assert.Equal(MetricFormatter.NotAvailable, _formatter.FormatInteger(null));
    }

    [Fact]
    public void Currency_uses_the_accounts_own_symbol()
    {
        Assert.Equal("£1,234.50", _formatter.FormatCurrency(1234.5m));
        Assert.Equal("$1,234.50", new MetricFormatter("en-US", "USD").FormatCurrency(1234.5m));
    }

    [Fact]
    public void An_unknown_currency_code_is_shown_rather_than_guessed()
        => Assert.Equal($"PLN{MetricFormatter.CodeSeparator}10.00", new MetricFormatter("en-GB", "PLN").FormatCurrency(10m));

    [Fact]
    public void Changes_are_signed()
    {
        Assert.Equal("+18.0%", _formatter.FormatSignedPercent(18m));
        Assert.StartsWith("−", _formatter.FormatSignedPercent(-18m));
    }

    [Fact]
    public void Change_descriptions_use_words_where_a_percentage_would_mislead()
    {
        Assert.Equal("New activity", _formatter.FormatChange(MetricDelta.Create(5m, 0m, MetricPolarity.Neutral)));
        Assert.Equal("Stopped", _formatter.FormatChange(MetricDelta.Create(0m, 5m, MetricPolarity.Neutral)));
        Assert.Equal("No comparison", _formatter.FormatChange(MetricDelta.Create(5m, null, MetricPolarity.Neutral)));
        Assert.Equal("No change", _formatter.FormatChange(MetricDelta.Create(5m, 5m, MetricPolarity.Neutral)));
    }

    [Fact]
    public void Fractional_conversions_keep_their_decimals_while_whole_counts_do_not()
    {
        Assert.Equal("1,500", _formatter.FormatInteger(1500m));
        Assert.Equal("12.50", _formatter.FormatInteger(12.5m));
    }
}
