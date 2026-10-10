using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MsAds.Auditor.Core.Export;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

/// <summary>Serves a report section as a CSV download.</summary>
public class ExportModel(IReportStore store, ILogger<ExportModel> logger) : PageModel
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
}
