using Microsoft.BingAds.V13.CustomerManagement;
using MsAds.Auditor.BingAds.Reporting;

namespace MsAds.Auditor.Tests.BingAds;

/// <summary>
/// Mapping an account's reporting time zone onto the one the Reporting API expects.
/// </summary>
/// <remarks>
/// Microsoft's two enumerations do not agree on every name, and a failure to map is silent — the
/// service just applies its own default and the report covers slightly the wrong days. These
/// tests pin the mapping for every value an account can actually have.
/// </remarks>
public class ReportTimeZoneTests
{
    [Fact]
    public void Every_time_zone_an_account_can_report_maps_to_a_reporting_time_zone()
    {
        var unmapped = Enum.GetNames<TimeZoneType>()
            .Where(name => BingAdsReportingDataSource.ResolveReportTimeZone(name) is null)
            .ToList();

        Assert.True(
            unmapped.Count == 0,
            "These account time zones have no reporting equivalent, so reports for accounts in them "
            + "would silently use the service default: " + string.Join(", ", unmapped));
    }

    [Theory]
    // The UK value, which is what matters most here.
    [InlineData("GreenwichMeanTimeDublinEdinburghLisbonLondon")]
    [InlineData("EasternTimeUSCanada")]
    [InlineData("PacificTimeUSCanadaTijuana")]
    // The three Microsoft spells differently between the two enumerations.
    [InlineData("SolomonIslandNewCaledonia")]
    [InlineData("IslamabadKarachiTashkent")]
    [InlineData("AthensBuckarestIstanbul")]
    public void Known_time_zones_resolve(string accountTimeZone)
        => Assert.NotNull(BingAdsReportingDataSource.ResolveReportTimeZone(accountTimeZone));

    [Fact]
    public void Matching_ignores_case()
        => Assert.NotNull(BingAdsReportingDataSource.ResolveReportTimeZone("easterntimeuscanada"));

    [Fact]
    public void An_unknown_value_resolves_to_null_so_the_caller_can_warn()
    {
        Assert.Null(BingAdsReportingDataSource.ResolveReportTimeZone("MiddleOfNowhere"));
        Assert.Null(BingAdsReportingDataSource.ResolveReportTimeZone(null));
        Assert.Null(BingAdsReportingDataSource.ResolveReportTimeZone("  "));
    }
}
