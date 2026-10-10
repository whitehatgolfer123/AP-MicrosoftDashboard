using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

/// <summary>
/// Base for the pages that display part of a report. Each resolves the report from the store, so
/// moving between the analysis sections never re-queries the API.
/// </summary>
public abstract class ReportPageModel(IReportStore store, IOptions<AuditorOptions> options) : PageModel
{
    protected IReportStore Store { get; } = store;

    public AuditorOptions Options { get; } = options.Value;

    /// <summary>The report being viewed; the most recent one unless an id is supplied.</summary>
    public AuditReport? Report { get; private set; }

    /// <summary>A specific report to display, rather than the latest.</summary>
    [BindProperty(SupportsGet = true, Name = "report")]
    public Guid? ReportId { get; set; }

    public bool HasReport => Report is not null;

    /// <summary>True when an explicit report id was supplied but no longer exists.</summary>
    public bool ReportExpired { get; private set; }

    /// <summary>
    /// Resolves <see cref="Report"/>. Called by each page's own handler rather than from a
    /// handler declared here: a base-class <c>OnGet</c> would compete with a derived page's
    /// <c>OnGetAsync</c> and make the handler ambiguous.
    /// </summary>
    protected void LoadReport()
    {
        if (ReportId is { } id && id != Guid.Empty)
        {
            Report = Store.Get(id);
            ReportExpired = Report is null;
            return;
        }

        Report = Store.Latest;
    }
}
