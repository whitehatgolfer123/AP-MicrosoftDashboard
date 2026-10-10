using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Ai;

namespace MsAds.Auditor.Web.Services;

/// <summary>An AI API key saved from the settings page, with the provider and model it belongs to.</summary>
public sealed record AiCredential
{
    public required AiProvider Provider { get; init; }

    public required string ApiKey { get; init; }

    /// <summary>Blank to use the provider's default model.</summary>
    public string? Model { get; init; }

    public DateTimeOffset SavedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The last four characters, so someone can tell which key is saved without the key being
    /// readable. Never expose any more of it than this.
    /// </summary>
    public string Hint => ApiKey.Length <= 4 ? "****" : "…" + ApiKey[^4..];
}

/// <summary>
/// Holds the optional AI API key entered on the settings page.
/// </summary>
/// <remarks>
/// A key is a credential, so it is encrypted at rest and never returned to the browser — the
/// settings page can say which provider is configured and show the last four characters, and
/// nothing else.
/// </remarks>
public interface IAiCredentialStore
{
    AiCredential? Read();

    void Write(AiCredential credential);

    void Clear();
}

/// <summary>
/// Persists the key to a file under the user's local application data, encrypted with ASP.NET
/// Core Data Protection.
/// </summary>
/// <remarks>
/// The same approach as the Microsoft Advertising refresh token: outside the repository, so it
/// cannot be committed, and unreadable without the Data Protection keys for this machine and
/// user account.
/// </remarks>
public sealed class ProtectedFileAiCredentialStore : IAiCredentialStore
{
    /// <summary>Scopes the protector so this ciphertext cannot be used for any other purpose.</summary>
    private const string Purpose = "MsAds.Auditor.AiApiKey.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<ProtectedFileAiCredentialStore> _logger;
    private readonly string _path;
    private readonly object _gate = new();

    public ProtectedFileAiCredentialStore(
        IDataProtectionProvider provider,
        ILogger<ProtectedFileAiCredentialStore> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MsAdsAuditor");

        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "ai-credential.dat");
    }

    public AiCredential? Read()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            try
            {
                var json = _protector.Unprotect(File.ReadAllText(_path));
                return JsonSerializer.Deserialize<AiCredential>(json);
            }
            catch (Exception ex)
            {
                // Keys rotated, a different machine, or a corrupt file. Never log the content:
                // discard it and let the user enter the key again.
                _logger.LogWarning(
                    "The saved AI API key could not be read ({Reason}); it has been discarded. AI "
                    + "commentary is off until a key is entered again. The audit is unaffected.",
                    ex.GetType().Name);

                Clear();
                return null;
            }
        }
    }

    public void Write(AiCredential credential)
    {
        lock (_gate)
        {
            File.WriteAllText(_path, _protector.Protect(JsonSerializer.Serialize(credential)));
        }

        _logger.LogInformation(
            "An AI API key was saved for {Provider}. AI commentary is now available.",
            credential.Provider);
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

/// <summary>
/// The AI settings in force: a key saved from the settings page wins over configuration.
/// </summary>
/// <remarks>
/// Configuration still works and is the right place for a deployed instance — <c>user-secrets</c>
/// or an environment variable. The saved key exists so that one person running the tool locally
/// can switch the feature on without editing anything.
/// </remarks>
public sealed class StoredAiOptionsAccessor(
    IOptions<AiOptions> configured,
    IAiCredentialStore store) : IAiOptionsAccessor
{
    public AiOptions Current
    {
        get
        {
            var fromConfig = configured.Value;
            var saved = store.Read();

            if (saved is null)
            {
                return fromConfig;
            }

            // Carry the non-credential settings across; only the provider, key and model are
            // things the settings page lets anyone change.
            return new AiOptions
            {
                Provider = saved.Provider,
                ApiKey = saved.ApiKey,
                Model = string.IsNullOrWhiteSpace(saved.Model) ? null : saved.Model,
                MaxTokens = fromConfig.MaxTokens,
                Automatic = fromConfig.Automatic,
            };
        }
    }
}
