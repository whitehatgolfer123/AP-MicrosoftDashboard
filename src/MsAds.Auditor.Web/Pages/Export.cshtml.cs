using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Export;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Reports.Documents;
using MsAds.Auditor.Reporting;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

/// <summary>Serves a report section as a CSV or PDF download.</summary>
public class ExportModel(
    IReportStore store,
    IReportRenderer renderer,
    IOptions<BrandOptions> brand,
    ILogger<ExportModel> logger) : PageModel
{
    public IActionResult OnGet(Guid report, string section)
    {
        var audit = store.Get(report);

        if (audit is null)
        {
            logger.LogWarning("Export requested for unknown report {ReportId}", report);
            return RedirectToPage("/History", new { expired = true });
        }

        if (!ReportCsvExporter.Available.Any(a => a.Key == section))
        {
            return BadRequest($"Unknown export section '{section}'.");
        }

        var csv = ReportCsvExporter.Export(audit, section);
        var fileName = ReportCsvExporter.FileName(audit, section);

        // A UTF-8 byte order mark so that Excel opens accented search terms correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

        logger.LogInformation(
            "Exported {Section} for report {ReportId} ({Bytes} bytes)", section, report, bytes.Length);

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>Serves an audit artefact as a PDF.</summary>
    /// <remarks>
    /// Streamed straight from memory rather than written to disk. These documents are a few
    /// hundred kilobytes, and a PDF of a client's performance has no business sitting in a
    /// temporary directory waiting to be cleaned up. Phase 23's Blob Storage note applies only
    /// to asynchronous delivery, which this is not.
    /// </remarks>
    public IActionResult OnGetDocument(Guid report, string artefact)
    {
        var audit = store.Get(report);

        if (audit is null)
        {
            logger.LogWarning("PDF requested for unknown report {ReportId}", report);
            return RedirectToPage("/History", new { expired = true });
        }

        if (!AuditDocumentBuilder.Available.Any(a => a.Key == artefact))
        {
            return BadRequest($"Unknown artefact '{artefact}'.");
        }

        if (!renderer.CanRender)
        {
            // A clear reason rather than a 500: the cause is an absent font package on the host,
            // which is fixable, and CSV export still works meanwhile.
            logger.LogWarning(
                "PDF export was requested but is unavailable: {Reason}", renderer.UnavailableReason);

            // UnavailableReason is non-null whenever CanRender is false, but the compiler
            // cannot see that across the interface, and a null here would be an empty 503.
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                renderer.UnavailableReason ?? "PDF export is unavailable on this machine.");
        }

        try
        {
            var document = AuditDocumentBuilder.Build(audit, artefact, brand.Value);
            var bytes = renderer.Render(document);

            logger.LogInformation(
                "Rendered {Artefact} PDF for report {ReportId} ({Bytes} bytes)",
                artefact, report, bytes.Length);

            return File(bytes, "application/pdf", document.FileStem + ".pdf");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Rendering the {Artefact} PDF failed", artefact);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                "The PDF could not be produced. The detail has been written to the application log.");
        }
    }
}
