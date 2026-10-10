namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Stops the application listening on a network-reachable address while sign-in is switched off.
/// </summary>
/// <remarks>
/// Without sign-in, anyone who can reach the port can read every client's performance data and
/// export it. Running that way on localhost is reasonable for development; doing it on an address
/// colleagues or the network can reach is not, and is easy to do by accident when changing
/// <c>ASPNETCORE_URLS</c>. This turns that accident into a startup failure with an explanation.
/// </remarks>
public static class NetworkExposureGuard
{
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    /// <summary>
    /// Throws when the configured addresses are reachable beyond this machine and sign-in is not
    /// enabled.
    /// </summary>
    /// <param name="addresses">The addresses the server will bind, e.g. from ASPNETCORE_URLS.</param>
    public static void Validate(IEnumerable<string> addresses, AppSignInOptions signIn)
    {
        if (signIn.Enabled || signIn.AllowAnonymousAccessOnNetwork)
        {
            return;
        }

        var exposed = addresses.Where(IsNetworkReachable).ToList();

        if (exposed.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to start. The application is set to listen on {string.Join(", ", exposed)}, "
            + "which is reachable beyond this machine, but sign-in is disabled — anyone who could "
            + "reach that address would see every client's data.\n\n"
            + "Either bind to localhost only, or configure sign-in:\n"
            + "  dotnet user-secrets set \"Auditor:SignIn:Enabled\" \"true\"\n"
            + "  dotnet user-secrets set \"Auditor:SignIn:TenantId\" \"<your directory id>\"\n"
            + "  dotnet user-secrets set \"Auditor:SignIn:ClientId\" \"<client id>\"\n"
            + "  dotnet user-secrets set \"Auditor:SignIn:ClientSecret\" \"<secret>\"\n\n"
            + "To run without sign-in anyway, set Auditor:SignIn:AllowAnonymousAccessOnNetwork to "
            + "true — only do that on a trusted, isolated network.");
    }

    /// <summary>True when an address would accept connections from other machines.</summary>
    internal static bool IsNetworkReachable(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        // Wildcard bindings accept traffic on every interface.
        if (address.Contains("//*", StringComparison.Ordinal)
            || address.Contains("//+", StringComparison.Ordinal)
            || address.Contains("//0.0.0.0", StringComparison.Ordinal)
            || address.Contains("//[::]", StringComparison.Ordinal))
        {
            return true;
        }

        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
        {
            // Unparseable: treat as exposed rather than assume it is safe.
            return true;
        }

        return !LoopbackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }
}
