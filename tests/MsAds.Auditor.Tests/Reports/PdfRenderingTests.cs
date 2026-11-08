using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Reports.Documents;
using MsAds.Auditor.Reporting;
using MsAds.Auditor.Tests.Support;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace MsAds.Auditor.Tests.Reports;

/// <summary>
/// Renders real documents to PDF and checks the output.
/// </summary>
/// <remarks>
/// PdfSharpCore needs a font on the host. Where none is present the renderer reports that
/// clearly instead of throwing, and these tests assert that behaviour rather than failing — a
/// CI runner without a font package should not turn the suite red over something the
/// application already handles gracefully.
/// </remarks>
public class PdfRenderingTests
{
    [Fact]
    public void The_renderer_says_whether_it_can_render()
    {
        var renderer = NewRenderer();

        // Exactly one of the two is true, and the reason is actionable when it cannot.
        Assert.Equal(renderer.CanRender, renderer.UnavailableReason is null);

        if (!renderer.CanRender)
        {
            Assert.Contains("font", renderer.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("executive")]
    [InlineData("account")]
    public void A_document_renders_to_a_valid_pdf(string key)
    {
        var renderer = NewRenderer();

        if (!renderer.CanRender)
        {
            return;
        }

        var report = TestData.DemoReport();
        var document = AuditDocumentBuilder.Build(report, key, new BrandOptions());

        var bytes = renderer.Render(document);

        // A PDF begins with %PDF- and ends with the end-of-file marker. Together these catch a
        // truncated or half-written stream, which is the failure mode that produces a file the
        // browser downloads and then cannot open.
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5), StringComparison.Ordinal);

        var tail = Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(1024, bytes.Length));
        Assert.Contains("%%EOF", tail, StringComparison.Ordinal);

        // Big enough to be a real document with embedded fonts, not an empty shell.
        Assert.True(bytes.Length > 10_000, $"Rendered {key} PDF was only {bytes.Length} bytes.");
    }

    [Fact]
    public void A_demo_report_is_marked_as_demo_in_the_document_metadata()
    {
        var renderer = NewRenderer();

        if (!renderer.CanRender)
        {
            return;
        }

        var report = TestData.DemoReport();
        Assert.True(report.IsDemo, "The demo data source should produce a demo report.");

        var document = AuditDocumentBuilder.Executive(report, new BrandOptions());

        // The provenance carries it, the title page shows a banner, and the footer repeats it.
        // A demo PDF reaching a client unlabelled is the worst thing this feature could do.
        Assert.True(document.Provenance.IsDemo);
        Assert.Contains("DEMO", document.FileStem, StringComparison.Ordinal);

        // The data-source row has to say so in words. It is not required to shout: where the
        // source names itself "Demo data (synthetic)" the marker is not appended again.
        var dataSource = document.Provenance.Rows().First(r => r.Key == "Data source").Value;

        Assert.Contains("demo", dataSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEMO FIGURES — SYNTHETIC", dataSource, StringComparison.Ordinal);

        var bytes = renderer.Render(document);

        // Read back rather than grepped: PdfSharpCore writes the info dictionary as a UTF-16
        // hex string, so searching the raw bytes for ASCII would pass or fail for the wrong
        // reason.
        using var stream = new MemoryStream(bytes);
        using var reopened = PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly);

        Assert.Contains("SYNTHETIC DEMO", reopened.Info.Subject, StringComparison.Ordinal);
        Assert.Equal("Add People", reopened.Info.Author);
    }

    [Fact]
    public void A_long_document_runs_to_several_pages()
    {
        var renderer = NewRenderer();

        if (!renderer.CanRender)
        {
            return;
        }

        var document = AuditDocumentBuilder.Account(TestData.DemoReport(), new BrandOptions());

        var bytes = renderer.Render(document);

        // The full audit carries campaigns, keywords, search terms and change history, so it
        // cannot fit on the title page alone. Counting page objects is crude but enough to show
        // pagination happened rather than content being drawn off the bottom of page one.
        var pages = CountOccurrences(Encoding.ASCII.GetString(bytes), "/Type /Page\n");
        Assert.True(pages >= 2, $"Expected the account audit to paginate; found {pages} page object(s).");
    }

    [Fact]
    public void Rendering_the_same_document_twice_gives_the_same_length()
    {
        var renderer = NewRenderer();

        if (!renderer.CanRender)
        {
            return;
        }

        var document = AuditDocumentBuilder.Executive(TestData.DemoReport(), new BrandOptions());

        // Not byte-equality: a PDF carries a creation timestamp. Equal length shows the layout
        // is deterministic, which is what makes a rendering regression visible.
        Assert.Equal(renderer.Render(document).Length, renderer.Render(document).Length);
    }

    [Fact]
    public void An_unknown_artefact_key_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AuditDocumentBuilder.Build(TestData.DemoReport(), "nonsense", new BrandOptions()));
    }

    [Fact]
    public void Every_advertised_artefact_can_be_built()
    {
        var report = TestData.DemoReport();

        // The download links are generated from Available, so an entry with no builder would be
        // a link that errors when clicked.
        foreach (var (key, label, description) in AuditDocumentBuilder.Available)
        {
            var document = AuditDocumentBuilder.Build(report, key, new BrandOptions());

            Assert.NotEmpty(document.Title);
            Assert.NotEmpty(document.Blocks);
            Assert.NotEmpty(label);
            Assert.NotEmpty(description);
        }
    }

    private static PdfReportRenderer NewRenderer()
        => new(
            new OptionsWrapper<BrandOptions>(new BrandOptions()),
            NullLogger<PdfReportRenderer>.Instance);

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
