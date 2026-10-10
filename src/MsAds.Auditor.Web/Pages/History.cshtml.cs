using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

public class HistoryModel(
    IReportStore store,
    IOptions<AuditorOptions> options,
    ISettingsStore settingsStore,
    AuditService auditService) : PageModel
{
    public IReadOnlyList<AuditReport> Reports { get; private set; } = [];

    public AuditorOptions Options { get; } = options.Value;

    public AuditOutcome? Outcome { get; private set; }

    /// <summary>Set when the user followed a link to a report that has since been dropped.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Expired { get; set; }

    public void OnGet() => Reports = store.All;

    /// <summary>Re-runs a stored report's account, period and analyses against current data.</summary>
    public async Task<IActionResult> OnPostRerunAsync(Guid id, CancellationToken cancellationToken)
    {
        var original = store.Get(id);

        if (original is null)
        {
            Reports = store.All;
            Expired = true;
            return Page();
        }

        var request = new AuditRequest
        {
            AccountId = original.Account.AccountId,
            Period = original.Period.Period,
            CompareWithPreviousPeriod = original.Period.HasComparison,
            ExcludeToday = original.Period.CurrentDayExcluded,
            Selection = original.Selection,
            Settings = settingsStore.Current,
        };

        Outcome = await auditService.RunAsync(request, cancellationToken).ConfigureAwait(false);

        if (Outcome.Succeeded)
        {
            return RedirectToPage("/Index", new { report = Outcome.Report!.ReportId });
        }

        Reports = store.All;
        return Page();
    }
}
