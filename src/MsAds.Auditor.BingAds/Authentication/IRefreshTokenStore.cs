namespace MsAds.Auditor.BingAds.Authentication;

/// <summary>
/// Holds the OAuth refresh token for the connected Microsoft Advertising user.
/// </summary>
/// <remarks>
/// A refresh token is a long-lived credential: it must never be logged, displayed, or written
/// to source control. Implementations either keep it in memory only, or encrypt it at rest.
/// </remarks>
public interface IRefreshTokenStore
{
    string? Read();

    void Write(string refreshToken);

    void Clear();

    /// <summary>Whether the token survives an application restart.</summary>
    bool IsPersistent { get; }
}

/// <summary>Keeps the refresh token in process memory only. Cleared on restart.</summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private string? _token;

    public bool IsPersistent => false;

    public string? Read() => _token;

    public void Write(string refreshToken) => _token = refreshToken;

    public void Clear() => _token = null;
}
