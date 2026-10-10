namespace MsAds.Auditor.BingAds;

/// <summary>
/// Microsoft Advertising API credentials and endpoints.
/// </summary>
/// <remarks>
/// Everything here except <see cref="Environment"/>, <see cref="Tenant"/> and
/// <see cref="RedirectUri"/> is a secret. Values are read from configuration providers — user
/// secrets in development, environment variables in a hosted environment — and are never
/// written to source control, logged, or shown in the interface.
/// </remarks>
public sealed class MicrosoftAdvertisingOptions
{
    /// <summary>The universal sandbox developer token, usable by anyone against sandbox.</summary>
    public const string SandboxDeveloperToken = "BBD37VB98";

    /// <summary>"Production" or "Sandbox". The two have entirely separate credentials and data.</summary>
    public string Environment { get; set; } = "Production";

    public bool IsSandbox => string.Equals(Environment, "Sandbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The developer token. Request a production one from
    /// https://ads.microsoft.com/cc/Settings/DevSettings while signed in as a Super Admin. The
    /// token is universal: one token covers every account the signed-in user can already reach,
    /// and it grants no permissions of its own.
    /// </summary>
    public string? DeveloperToken { get; set; }

    /// <summary>The Entra ID application (client) ID.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The application secret. Required: Microsoft's guidance is that a refresh token issued to
    /// a public client is bound to the device that granted it, so a server-side application must
    /// register as a web application with a secret.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>Must match a redirect URI registered on the application exactly.</summary>
    public string RedirectUri { get; set; } = "http://localhost:5114/signin-microsoftads";

    /// <summary>
    /// The tenant segment of the authority. "common" accepts both work or school accounts and
    /// personal Microsoft accounts, which is what the Microsoft Advertising guidance recommends.
    /// </summary>
    public string Tenant { get; set; } = "common";

    /// <summary>
    /// Keep the refresh token across restarts, encrypted at rest with ASP.NET Core Data
    /// Protection. Turn this off to hold it in memory only, at the cost of re-consenting after
    /// every restart.
    /// </summary>
    public bool PersistRefreshToken { get; set; } = true;

    /// <summary>The effective developer token, defaulting to the universal one in sandbox.</summary>
    public string? EffectiveDeveloperToken => IsSandbox && string.IsNullOrWhiteSpace(DeveloperToken)
        ? SandboxDeveloperToken
        : DeveloperToken;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(EffectiveDeveloperToken)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>Names the settings still missing, for the connection status panel.</summary>
    public IReadOnlyList<string> MissingSettings()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(EffectiveDeveloperToken)) missing.Add("Developer token");
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add("Application (client) ID");
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add("Client secret");

        return missing;
    }
}
