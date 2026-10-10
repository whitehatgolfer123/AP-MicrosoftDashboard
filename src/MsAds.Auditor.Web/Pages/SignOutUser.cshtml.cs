using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

/// <summary>
/// Signs the current person out of the application.
/// </summary>
/// <remarks>
/// This clears the local session and signs out at Entra ID. It deliberately does not touch the
/// Microsoft Advertising connection — that is a shared, long-lived authorisation managed on the
/// Connection page, not something an individual's session owns.
/// </remarks>
[AllowAnonymous]
public class SignOutUserModel(AppSignInOptions signIn) : PageModel
{
    public IActionResult OnGet()
    {
        if (!signIn.Enabled)
        {
            return RedirectToPage("/Index");
        }

        var properties = new AuthenticationProperties { RedirectUri = Url.Page("/SignedOut") };

        return SignOut(
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
