using Microsoft.BingAds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MsAds.Auditor.BingAds.Authentication;

/// <summary>Why the application cannot currently talk to Microsoft Advertising.</summary>
public enum ConnectionState
{
    /// <summary>Credentials are present and a refresh token is held.</summary>
    Connected,

    /// <summary>Developer token, client ID or client secret has not been configured.</summary>
    NotConfigured,

    /// <summary>Configured, but no user has granted consent yet.</summary>
    NotSignedIn,

    /// <summary>Consent was granted but the refresh token is no longer accepted.</summary>
    ConsentExpired,
}

public sealed record ConnectionStatus(
    ConnectionState State,
    string Message,
    IReadOnlyList<string> MissingSettings,
    string? SignedInUser = null)
{
    public bool IsUsable => State == ConnectionState.Connected;
}

/// <summary>
/// Owns the OAuth authorisation-code flow against the Microsoft identity platform and hands out
/// the <see cref="AuthorizationData"/> that every API call needs.
/// </summary>
/// <remarks>
/// The flow is: send the user to <see cref="GetAuthorizationUrl"/>, receive the code on the
/// redirect URI, exchange it with <see cref="CompleteSignInAsync"/>, then keep using the refresh
/// token. The SDK refreshes the access token on our behalf; we persist each new refresh token
/// because Microsoft may rotate it.
/// </remarks>
public sealed class BingAdsAuthenticationService(
    IOptions<MicrosoftAdvertisingOptions> options,
    IRefreshTokenStore tokenStore,
    ILogger<BingAdsAuthenticationService> logger)
{
    private readonly MicrosoftAdvertisingOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The display name of the signed-in user, once known. Never a credential.</summary>
    public string? SignedInUserName { get; private set; }

    public ApiEnvironment Environment =>
        _options.IsSandbox ? ApiEnvironment.Sandbox : ApiEnvironment.Production;

    public ConnectionStatus GetStatus()
    {
        var missing = _options.MissingSettings();

        if (missing.Count > 0)
        {
            return new ConnectionStatus(
                ConnectionState.NotConfigured,
                "Microsoft Advertising credentials have not been configured: " + string.Join(", ", missing) + ".",
                missing);
        }

        return tokenStore.Read() is null
            ? new ConnectionStatus(
                ConnectionState.NotSignedIn,
                "Credentials are configured, but no Microsoft Advertising user has granted consent yet.",
                missing)
            : new ConnectionStatus(
                ConnectionState.Connected,
                "Connected to Microsoft Advertising.",
                missing,
                SignedInUserName);
    }

    /// <summary>
    /// The URL to send the user to for consent. <paramref name="state"/> is echoed back on the
    /// redirect and must be verified, to prevent a forged callback.
    /// </summary>
    /// <remarks>
    /// Returned as a fully escaped string. The SDK builds the endpoint with a literal space
    /// between the two scopes, which <see cref="Uri.ToString"/> preserves; sending that in a
    /// Location header would produce an invalid redirect. <see cref="Uri.AbsoluteUri"/> escapes
    /// it, so this method returns that and callers cannot pick the wrong one.
    /// </remarks>
    public string GetAuthorizationUrl(string state)
    {
        var grant = CreateGrant(refreshToken: null);
        grant.State = state;
        return grant.GetAuthorizationEndpoint().AbsoluteUri;
    }

    /// <summary>
    /// Exchanges the authorisation code returned on the redirect URI for access and refresh
    /// tokens, and stores the refresh token.
    /// </summary>
    public async Task CompleteSignInAsync(string code, CancellationToken cancellationToken = default)
    {
        var grant = CreateGrant(refreshToken: null);

        var tokens = await grant
            .RequestAccessAndRefreshTokensAsync(code)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            throw new InvalidOperationException(
                "Microsoft returned no refresh token. Check that the application is registered as a web "
                + "application with a client secret and that the 'offline_access' scope was granted.");
        }

        tokenStore.Write(tokens.RefreshToken);
        logger.LogInformation("Microsoft Advertising connection established; refresh token stored.");
    }

    /// <summary>Forgets the stored refresh token, disconnecting the application.</summary>
    public void SignOut()
    {
        tokenStore.Clear();
        SignedInUserName = null;
        logger.LogInformation("Microsoft Advertising connection cleared.");
    }

    /// <summary>
    /// Builds authorisation data for an API call. <paramref name="accountId"/> and
    /// <paramref name="customerId"/> are left at zero for calls that do not target an account,
    /// such as the initial account discovery.
    /// </summary>
    public async Task<AuthorizationData> GetAuthorizationDataAsync(
        long accountId = 0, long customerId = 0, CancellationToken cancellationToken = default)
    {
        var refreshToken = tokenStore.Read()
            ?? throw new BingAdsNotConnectedException(
                "No Microsoft Advertising user is connected. Sign in before requesting data.");

        // Serialised because a token refresh rotates the stored value, and two concurrent
        // refreshes would race to persist different tokens.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var grant = CreateGrant(refreshToken);

            try
            {
                var tokens = await grant
                    .RequestAccessAndRefreshTokensAsync(refreshToken)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Microsoft may issue a new refresh token; the old one must be discarded.
                if (!string.IsNullOrWhiteSpace(tokens.RefreshToken) && tokens.RefreshToken != refreshToken)
                {
                    tokenStore.Write(tokens.RefreshToken);
                }
            }
            catch (OAuthTokenRequestException ex)
            {
                // The grant is gone: password change, revoked consent, or an expired token.
                tokenStore.Clear();

                throw new BingAdsNotConnectedException(
                    "Microsoft Advertising rejected the stored sign-in, so it has been cleared. This "
                    + "happens when consent is revoked, the password changes, or the refresh token "
                    + "expires. Reconnect to continue.", ex);
            }

            return new AuthorizationData
            {
                Authentication = grant,
                DeveloperToken = _options.EffectiveDeveloperToken,
                AccountId = accountId,
                CustomerId = customerId,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    internal void RecordSignedInUser(string? userName) => SignedInUserName = userName;

    private OAuthWebAuthCodeGrant CreateGrant(string? refreshToken)
    {
        if (!_options.IsConfigured)
        {
            throw new BingAdsNotConnectedException(
                "Microsoft Advertising credentials are not configured: "
                + string.Join(", ", _options.MissingSettings()) + ".");
        }

        var redirect = new Uri(_options.RedirectUri);

        var grant = refreshToken is null
            ? new OAuthWebAuthCodeGrant(
                _options.ClientId, _options.ClientSecret, redirect,
                Environment, OAuthScope.MSADS_MANAGE, _options.Tenant)
            : new OAuthWebAuthCodeGrant(
                _options.ClientId, _options.ClientSecret, redirect, refreshToken,
                Environment, OAuthScope.MSADS_MANAGE, _options.Tenant);

        return grant;
    }
}

/// <summary>Raised when an API call is attempted without a usable Microsoft Advertising connection.</summary>
public sealed class BingAdsNotConnectedException(string message, Exception? inner = null)
    : Exception(message, inner);
