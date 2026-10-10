using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MsAds.Auditor.BingAds;
using MsAds.Auditor.BingAds.Authentication;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

/// <summary>
/// Starts and ends the Microsoft Advertising connection, and reports what is still missing.
/// </summary>
public class ConnectModel(
    BingAdsAuthenticationService authentication,
    IOptions<AuditorOptions> auditorOptions,
    IOptions<MicrosoftAdvertisingOptions> adsOptions,
    IAccountDirectory accountDirectory,
    AppSignInOptions appSignIn,
    ILogger<ConnectModel> logger) : PageModel
{
    /// <summary>Session key holding the OAuth state value across the redirect.</summary>
    public const string StateSessionKey = "msads-oauth-state";

    public AuditorOptions Options { get; } = auditorOptions.Value;

    public MicrosoftAdvertisingOptions Ads { get; } = adsOptions.Value;

    /// <summary>Controls who may open the application, as distinct from the API connection.</summary>
    public AppSignInOptions AppSignIn { get; } = appSignIn;

    public ConnectionStatus Status { get; private set; } = null!;

    public bool IsDemoMode => Options.Mode == AuditorMode.Demo;

    /// <summary>Accounts visible to the connected user, loaded once connected.</summary>
    public IReadOnlyList<AdAccountInfo> Accounts { get; private set; } = [];

    public string? AccountsError { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Status = authentication.GetStatus();

        if (Status.IsUsable && !IsDemoMode)
        {
            try
            {
                Accounts = await accountDirectory.ListAccessibleAccountsAsync(cancellationToken).ConfigureAwait(false);
                // Refresh the status so the signed-in user name, learned during the listing, shows.
                Status = authentication.GetStatus();
            }
            catch (BingAdsNotConnectedException ex)
            {
                AccountsError = ex.Message;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not list accounts after connecting");
                AccountsError = "Connected, but the account list could not be retrieved. This is usually temporary.";
            }
        }
    }

    /// <summary>Sends the user to Microsoft to grant consent.</summary>
    public IActionResult OnPostConnect()
    {
        if (!Ads.IsConfigured)
        {
            return RedirectToPage(new { error = "Credentials are not configured yet." });
        }

        // A random state, checked on return, so a callback cannot be forged.
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        HttpContext.Session.SetString(StateSessionKey, state);

        logger.LogInformation("Redirecting to Microsoft for consent.");

        return Redirect(authentication.GetAuthorizationUrl(state));
    }

    public IActionResult OnPostDisconnect()
    {
        authentication.SignOut();

        if (accountDirectory is BingAdsAccountDirectory live)
        {
            live.InvalidateCache();
        }

        return RedirectToPage(new { message = "Disconnected from Microsoft Advertising." });
    }

    public IActionResult OnPostRefresh()
    {
        if (accountDirectory is BingAdsAccountDirectory live)
        {
            live.InvalidateCache();
        }

        return RedirectToPage(new { message = "Account list refreshed." });
    }
}
