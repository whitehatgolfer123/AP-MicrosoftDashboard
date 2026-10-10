using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsAds.Auditor.BingAds;
using MsAds.Auditor.BingAds.Authentication;

namespace MsAds.Auditor.Tests.BingAds;

/// <summary>
/// The consent URL the application sends people to. Checked without credentials so that a
/// mistake in the scope, tenant or redirect URI is caught here rather than as an opaque
/// AADSTS error during the first real sign-in.
/// </summary>
public class AuthorizationUrlTests
{
    private static BingAdsAuthenticationService Service(MicrosoftAdvertisingOptions? options = null)
        => new(
            Options.Create(options ?? new MicrosoftAdvertisingOptions
            {
                DeveloperToken = "test-developer-token",
                ClientId = "11111111-2222-3333-4444-555555555555",
                ClientSecret = "test-secret",
            }),
            new InMemoryRefreshTokenStore(),
            NullLogger<BingAdsAuthenticationService>.Instance);

    [Fact]
    public void The_consent_url_requests_the_scope_that_satisfies_the_mfa_requirement()
    {
        var url = Service().GetAuthorizationUrl("state-123");

        // msads.manage is the scope Microsoft requires post-MFA-enforcement; an older scope
        // would be accepted at sign-in and then rejected by every API call.
        Assert.Contains("msads.manage", url);
    }

    [Fact]
    public void The_consent_url_targets_the_microsoft_identity_platform_endpoint()
    {
        var url = Service().GetAuthorizationUrl("state-123");

        Assert.StartsWith("https://login.microsoftonline.com/", url);
        Assert.Contains("/oauth2/v2.0/authorize", url);
    }

    [Fact]
    public void The_consent_url_uses_the_common_tenant_so_work_and_personal_accounts_both_work()
        => Assert.Contains("/common/", Service().GetAuthorizationUrl("s"));

    [Fact]
    public void The_consent_url_carries_the_client_id_redirect_uri_and_state()
    {
        var url = Service().GetAuthorizationUrl("state-123");

        Assert.Contains("11111111-2222-3333-4444-555555555555", url);
        Assert.Contains("state-123", url);
        Assert.Contains("redirect_uri=http://localhost:5114/signin-microsoftads", url);
    }

    [Fact]
    public void The_consent_url_contains_no_unescaped_space()
    {
        // The SDK separates the two scopes with a literal space. Left unescaped it would make an
        // invalid Location header and the very first sign-in would fail.
        var url = Service().GetAuthorizationUrl("state-123");

        Assert.DoesNotContain(" ", url);
        Assert.Contains("msads.manage%20offline_access", url);
    }

    [Fact]
    public void The_consent_url_requests_offline_access_so_a_refresh_token_is_issued()
        => Assert.Contains("offline_access", Service().GetAuthorizationUrl("s"));

    [Fact]
    public void The_consent_url_asks_for_an_authorization_code()
        => Assert.Contains("response_type=code", Service().GetAuthorizationUrl("s"));

    [Fact]
    public void A_configured_redirect_uri_is_used_verbatim_so_it_can_match_the_registration()
    {
        var service = Service(new MicrosoftAdvertisingOptions
        {
            DeveloperToken = "t",
            ClientId = "c",
            ClientSecret = "s",
            RedirectUri = "https://auditor.example.com/signin-microsoftads",
        });

        Assert.Contains(
            "redirect_uri=https://auditor.example.com/signin-microsoftads",
            service.GetAuthorizationUrl("s"));
    }

    [Fact]
    public void Sandbox_supplies_the_universal_developer_token_automatically()
    {
        var sandbox = new MicrosoftAdvertisingOptions { Environment = "Sandbox", ClientId = "c", ClientSecret = "s" };

        Assert.True(sandbox.IsSandbox);
        Assert.Equal(MicrosoftAdvertisingOptions.SandboxDeveloperToken, sandbox.EffectiveDeveloperToken);
        Assert.True(sandbox.IsConfigured);
    }

    [Fact]
    public void Asking_for_data_before_anyone_has_connected_fails_with_a_clear_message()
    {
        var exception = Assert.ThrowsAsync<BingAdsNotConnectedException>(
            () => Service().GetAuthorizationDataAsync()).Result;

        Assert.Contains("No Microsoft Advertising user is connected", exception.Message);
    }
}
