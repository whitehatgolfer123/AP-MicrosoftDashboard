using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MsAds.Auditor.BingAds.Authentication;

namespace MsAds.Auditor.Web.Pages;

/// <summary>
/// Receives the OAuth redirect from Microsoft and exchanges the authorisation code for tokens.
/// </summary>
/// <remarks>
/// The route must match the redirect URI registered on the Entra ID application exactly.
/// </remarks>
public class SignInCallbackModel(
    BingAdsAuthenticationService authentication,
    ILogger<SignInCallbackModel> logger) : PageModel
{
    public async Task<IActionResult> OnGetAsync(
        string? code, string? state, string? error, string? error_description,
        CancellationToken cancellationToken)
    {
        // Microsoft reports a refusal or failure on the query string rather than by status code.
        if (!string.IsNullOrEmpty(error))
        {
            logger.LogWarning("Consent was not granted: {Error}", error);

            return RedirectToPage("/Connect", new
            {
                error = $"Microsoft did not grant access ({error}). {error_description}",
            });
        }

        var expected = HttpContext.Session.GetString(ConnectModel.StateSessionKey);
        HttpContext.Session.Remove(ConnectModel.StateSessionKey);

        // An unexpected state means this callback did not originate from a sign-in we started.
        if (string.IsNullOrEmpty(expected) || !string.Equals(expected, state, StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected an OAuth callback whose state did not match.");

            return RedirectToPage("/Connect", new
            {
                error = "The sign-in response did not match the request, so it was rejected. "
                        + "Start the connection again from this page.",
            });
        }

        if (string.IsNullOrEmpty(code))
        {
            return RedirectToPage("/Connect", new { error = "Microsoft returned no authorisation code." });
        }

        try
        {
            await authentication.CompleteSignInAsync(code, cancellationToken).ConfigureAwait(false);
            return RedirectToPage("/Connect", new { message = "Connected to Microsoft Advertising." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exchanging the authorisation code failed");

            return RedirectToPage("/Connect", new
            {
                error = "The authorisation code could not be exchanged for a token. Check that the client "
                        + "secret is correct and that the redirect URI registered on the application matches "
                        + "this one exactly.",
            });
        }
    }
}
