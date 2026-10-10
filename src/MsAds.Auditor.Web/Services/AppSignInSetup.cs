using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Wires up Entra ID sign-in for the application itself.
/// </summary>
public static class AppSignInSetup
{
    /// <summary>Where a rejected user is sent.</summary>
    public const string AccessDeniedPath = "/AccessDenied";

    /// <summary>
    /// Adds cookie plus OpenID Connect authentication when sign-in is enabled, and makes every
    /// page require an authenticated user.
    /// </summary>
    public static IServiceCollection AddAppSignIn(this IServiceCollection services, AppSignInOptions options)
    {
        if (!options.Enabled)
        {
            // Nothing is registered, so no authentication runs and every page stays anonymous.
            // NetworkExposureGuard prevents this combination reaching the network.
            return services;
        }

        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Sign-in is enabled but not configured. Missing: "
                + string.Join(", ", options.MissingSettings())
                + ". Set these with user secrets or environment variables.");
        }

        services
            .AddAuthentication(sharedOptions =>
            {
                sharedOptions.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                sharedOptions.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = "MsAdsAuditor.Session";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
                cookie.SlidingExpiration = true;
                cookie.AccessDeniedPath = AccessDeniedPath;
            })
            .AddOpenIdConnect(oidc =>
            {
                // Pinning the authority to one directory is the access restriction: accounts
                // outside this tenant cannot obtain a token for the application at all.
                oidc.Authority = $"https://login.microsoftonline.com/{options.TenantId}/v2.0";
                oidc.ClientId = options.ClientId;
                oidc.ClientSecret = options.ClientSecret;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;

                // Identity only. The application needs to know who someone is; it calls no API
                // on their behalf, so no access token is requested or stored.
                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.Scope.Add("profile");
                oidc.Scope.Add("email");
                oidc.SaveTokens = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;

                oidc.CallbackPath = "/signin-oidc";
                oidc.SignedOutCallbackPath = "/signout-callback-oidc";
                oidc.AccessDeniedPath = AccessDeniedPath;

                oidc.TokenValidationParameters.NameClaimType = "name";

                oidc.Events.OnTokenValidated = context =>
                {
                    var principal = context.Principal;

                    var upn = principal?.FindFirstValue("preferred_username")
                              ?? principal?.FindFirstValue(ClaimTypes.Upn);
                    var email = principal?.FindFirstValue("email")
                                ?? principal?.FindFirstValue(ClaimTypes.Email);

                    if (!options.IsUserAllowed(upn, email))
                    {
                        // In the directory but not on the allow-list.
                        context.Fail("This account is not permitted to use the auditor.");
                    }

                    return Task.CompletedTask;
                };

                oidc.Events.OnRemoteFailure = context =>
                {
                    // Surface a readable page rather than an unhandled exception.
                    context.Response.Redirect(AccessDeniedPath);
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization(authorization =>
        {
            // Everything requires sign-in unless a page opts out with [AllowAnonymous].
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    /// <summary>Inserts the authentication middleware when sign-in is enabled.</summary>
    public static WebApplication UseAppSignIn(this WebApplication app, AppSignInOptions options)
    {
        if (!options.Enabled)
        {
            return app;
        }

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
