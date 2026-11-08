using MsAds.Auditor.Core.Reports.Documents;

namespace MsAds.Auditor.Tests.Reports;

/// <summary>
/// The provenance block that appears on every artefact.
/// </summary>
public class DocumentProvenanceTests
{
    [Fact]
    public void A_demo_source_that_already_says_so_is_not_marked_twice()
    {
        var rows = Provenance("Demo data (synthetic)", isDemo: true).Rows();

        Assert.Equal(
            "Demo data (synthetic)",
            rows.First(r => r.Key == "Data source").Value);
    }

    [Fact]
    public void A_demo_source_that_does_not_say_so_is_marked()
    {
        // Guards the case that matters: a source whose name gives no hint must not produce a
        // document that looks live.
        var rows = Provenance("Internal fixture", isDemo: true).Rows();

        Assert.Contains(
            "SYNTHETIC DEMO FIGURES",
            rows.First(r => r.Key == "Data source").Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_live_source_is_never_marked_as_demo()
    {
        var rows = Provenance("Microsoft Advertising API", isDemo: false).Rows();

        Assert.Equal(
            "Microsoft Advertising API",
            rows.First(r => r.Key == "Data source").Value);
    }

    [Fact]
    public void Absent_values_are_left_out_rather_than_shown_empty()
    {
        // A portfolio document has no single account, so those rows should not appear at all
        // rather than appear blank.
        var rows = Provenance("Demo data", isDemo: true).Rows();

        Assert.DoesNotContain(rows, r => r.Key == "Account");
        Assert.DoesNotContain(rows, r => string.IsNullOrWhiteSpace(r.Value));
    }

    [Fact]
    public void The_rows_always_carry_what_phase_23_requires()
    {
        var provenance = new DocumentProvenance
        {
            ProductName = "Auditor",
            GeneratedAt = "16 Mar 2026 10:30 UTC",
            DataSource = "Microsoft Advertising API",
            IsDemo = false,
            AccountName = "Northern Plumbing",
            AccountId = "1234567",
            CustomerId = "7654321",
            Currency = "GBP",
            Period = "14 Feb 2026 – 15 Mar 2026 (30 days)",
            TimeZone = "Europe/London",
            Reconciliation = "Not reconciled.",
            Methodology = "Deterministic rules.",
        };

        var keys = provenance.Rows().Select(r => r.Key).ToList();

        // A printed page outlives the screen it came from, so none of these may go missing.
        Assert.Contains("Account", keys);
        Assert.Contains("Manager (customer) id", keys);
        Assert.Contains("Reporting period", keys);
        Assert.Contains("Reporting time zone", keys);
        Assert.Contains("Currency", keys);
        Assert.Contains("Data source", keys);
        Assert.Contains("Reconciliation", keys);
        Assert.Contains("Generated", keys);
    }

    private static DocumentProvenance Provenance(string source, bool isDemo) => new()
    {
        ProductName = "Auditor",
        GeneratedAt = "16 Mar 2026 10:30 UTC",
        DataSource = source,
        IsDemo = isDemo,
        Reconciliation = "Not applicable.",
        Methodology = "Deterministic rules.",
    };
}
