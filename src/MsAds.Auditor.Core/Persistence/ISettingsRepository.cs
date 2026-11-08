using MsAds.Auditor.Core.Settings;

namespace MsAds.Auditor.Core.Persistence;

/// <summary>
/// Reads and writes the analysis thresholds that are currently in force.
/// </summary>
/// <remarks>
/// A deliberately small seam. The web layer keeps its own <c>ISettingsStore</c> for the in-memory
/// working copy the pages read on every request; this is only the durable backing behind it, so
/// settings entered in the Settings screen survive a restart rather than reverting to
/// configuration. Settings are operator preferences, not client data, so persisting them raises
/// none of the privacy questions that persisting performance figures would.
/// </remarks>
public interface ISettingsRepository
{
    /// <summary>
    /// The stored settings, or null when nothing has been saved and configuration defaults
    /// should be used instead.
    /// </summary>
    Task<AuditSettings?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the settings. <paramref name="updatedBy"/> is the signed-in user where sign-in is
    /// enabled, recorded so a threshold change is attributable; null otherwise.
    /// </summary>
    Task SaveAsync(
        AuditSettings settings, string? updatedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the stored settings, so the configured defaults apply again on the next load.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Used when persistence is switched off. Holds nothing, so configuration remains the source of
/// truth across restarts, which is the behaviour the application had before persistence existed.
/// </summary>
public sealed class NullSettingsRepository : ISettingsRepository
{
    public Task<AuditSettings?> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AuditSettings?>(null);

    public Task SaveAsync(
        AuditSettings settings, string? updatedBy = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ClearAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
