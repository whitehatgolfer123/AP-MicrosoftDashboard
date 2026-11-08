using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

/// <summary>
/// A direction-of-travel reading across every account the connected user can reach, so the
/// accounts worth a full audit can be picked out quickly.
/// </summary>
public class ScanModel(
    IAccountDirectory accountDirectory,
    PortfolioScanRunner runner,
    IScanStore scanStore,
    IOptions<AuditorOptions> options,
    ILogger<ScanModel> logger) : PageModel
{
    public AuditorOptions Options { get; } = options.Value;

    public IReadOnlyList<AdAccountInfo> AvailableAccounts { get; private set; } = [];

    public PortfolioScan? Scan => scanStore.Latest;

    /// <summary>
    /// Accounts whose verdict moved since the previous recorded scan, deteriorations first.
    /// Empty when there is no earlier scan to compare against.
    /// </summary>
    public IReadOnlyList<VerdictChange> Changes { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    [BindProperty]
    public ReportingPeriod Period { get; set; } = ReportingPeriod.Last7Days;

    /// <summary>Accounts to include. Every account is selected by default.</summary>
    [BindProperty]
    public List<long> SelectedAccountIds { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);

        // Default to a full sweep; unticking is the exception rather than the rule.
        SelectedAccountIds = [.. AvailableAccounts.Select(a => a.AccountId)];

        if (Scan is not null)
        {
            Period = Scan.Period.Period;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);

        var chosen = AvailableAccounts.Where(a => SelectedAccountIds.Contains(a.AccountId)).ToList();

        if (chosen.Count == 0)
        {
            ErrorMessage = "Select at least one account to scan.";
            return Page();
        }

        // The same runner the background schedule uses: it resolves the dates, applies the
        // account concurrency limit, records the run and works out what has changed.
        var outcome = await runner
            .RunAsync(chosen, Period, ScanTrigger.Manual, cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            ErrorMessage = outcome.FailureMessage;
            return Page();
        }

        scanStore.Set(outcome.Scan!);
        Changes = outcome.Changes;

        return Page();
    }

    private async Task LoadAccountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            AvailableAccounts = await accountDirectory
                .ListAccessibleAccountsAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not list accounts for the scan");
            AvailableAccounts = [];
            ErrorMessage = "The list of accessible accounts could not be retrieved.";
        }
    }
}
