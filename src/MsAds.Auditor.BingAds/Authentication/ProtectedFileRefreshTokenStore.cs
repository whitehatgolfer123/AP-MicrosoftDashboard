using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace MsAds.Auditor.BingAds.Authentication;

/// <summary>
/// Persists the refresh token to a file, encrypted with ASP.NET Core Data Protection so that the
/// token is never readable on disk. Without this the user would have to grant consent again on
/// every restart.
/// </summary>
/// <remarks>
/// The file lives under the user's local application data, outside the repository, and the
/// protection keys are managed by Data Protection, so the ciphertext is useless on another
/// machine or to another user account.
/// </remarks>
public sealed class ProtectedFileRefreshTokenStore : IRefreshTokenStore
{
    /// <summary>Scopes the protector so this ciphertext cannot be used for any other purpose.</summary>
    private const string Purpose = "MsAds.Auditor.RefreshToken.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<ProtectedFileRefreshTokenStore> _logger;
    private readonly string _path;
    private readonly object _gate = new();

    public ProtectedFileRefreshTokenStore(
        IDataProtectionProvider provider,
        ILogger<ProtectedFileRefreshTokenStore> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MsAdsAuditor");

        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "refresh-token.dat");
    }

    public bool IsPersistent => true;

    public string? Read()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                return _protector.Unprotect(File.ReadAllText(_path));
            }
            catch (Exception ex)
            {
                // Keys rotated, a different machine, or a corrupt file. Never log the content:
                // discard it and make the user reconnect.
                _logger.LogWarning(
                    "The stored refresh token could not be decrypted ({Reason}); it has been discarded "
                    + "and the Microsoft Advertising connection must be re-established.", ex.GetType().Name);

                Clear();
                return null;
            }
        }
    }

    public void Write(string refreshToken)
    {
        lock (_gate)
        {
            File.WriteAllText(_path, _protector.Protect(refreshToken));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
