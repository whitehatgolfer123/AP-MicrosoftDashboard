namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Controls who may open the application itself.
/// </summary>
/// <remarks>
/// <para>
/// This is separate from, and unrelated to, the Microsoft Advertising connection. That connection
/// authorises the application to <em>read advertising data</em> using one shared account; this
/// controls which <em>people</em> are allowed to see that data once it is on screen.
/// </para>
/// <para>
/// Sign-in is single-tenant: the authority is pinned to <see cref="TenantId"/>, so only accounts
/// in that Entra ID directory can authenticate at all. <see cref="AllowedUsers"/> narrows it
/// further when only some of the directory should have access.
/// </para>
/// </remarks>
public sealed class AppSignInOptions
{
    public const string SectionName = "Auditor:SignIn";

    /// <summary>
    /// Require people to sign in. Off by default so a fresh checkout runs on localhost with no
    /// configuration; the application refuses to listen on a non-loopback address while it is off.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The Entra ID directory (tenant) ID allowed to sign in. This is the access restriction:
    /// accounts outside this directory cannot authenticate.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Application (client) ID of the Entra ID registration used for sign-in. May be the same
    /// registration as the Microsoft Advertising one, provided both redirect URIs are added to it.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>The client secret for that registration.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optional allow-list of user principal names or email addresses. When empty, anyone in the
    /// tenant may sign in. Matching is case-insensitive.
    /// </summary>
    public IList<string> AllowedUsers { get; set; } = [];

    /// <summary>
    /// Permit running without sign-in on an address other than loopback. Deliberately awkward:
    /// turning this on publishes every client's performance data to anyone who can reach the port.
    /// </summary>
    public bool AllowAnonymousAccessOnNetwork { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    public IReadOnlyList<string> MissingSettings()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(TenantId)) missing.Add("Directory (tenant) ID");
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add("Application (client) ID");
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add("Client secret");

        return missing;
    }

    /// <summary>
    /// Whether a signed-in user is permitted. Checked against the allow-list on every sign-in, so
    /// removing someone from the list takes effect without touching the directory.
    /// </summary>
    public bool IsUserAllowed(string? userPrincipalName, string? email)
    {
        if (AllowedUsers.Count == 0)
        {
            return true;
        }

        return AllowedUsers.Any(allowed =>
            !string.IsNullOrWhiteSpace(allowed)
            && (string.Equals(allowed.Trim(), userPrincipalName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(allowed.Trim(), email, StringComparison.OrdinalIgnoreCase)));
    }
}
