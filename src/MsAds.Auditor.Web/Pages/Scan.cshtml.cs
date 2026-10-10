using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

/// <summary>
/// A direction-of-travel reading across every account the connected user can reach, so the
/// accounts worth a full audit can be picked out quickly.
/// </summary>
public class ScanModel(
    IAccountDirectory accountDirectory,
    PortfolioScanner scanner,
    ReportingPeriodResolver periodResolver,
    ISettingsStore settingsStore,
    IScanStore scanStore,
    IOptions<AuditorOptions> options,
    ILogger<ScanModel> logger) : PageModel
{
    public AuditorOptions Options { get; } = options.Value;

    public IReadOnlyList<AdAccountInfo> AvailableAccounts { get; private set; } = [];

    public PortfolioScan? Scan => scanStore.Latest;

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

        try
        {
            // Dates are resolved once, from the first account's time zone, so every account in
            // the scan covers the same calendar days and the comparison is like for like.
            var timeZone = AccountTimeZoneResolver.Resolve(chosen[0].TimeZone);
            var period = periodResolver.Resolve(Period, timeZone);

            var scan = await scanner
                .ScanAsync(chosen, period, settingsStore.Current, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            scanStore.Set(scan);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Portfolio scan failed");
            ErrorMessage = "The scan could not be completed. The detail has been written to the application log.";
        }

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
