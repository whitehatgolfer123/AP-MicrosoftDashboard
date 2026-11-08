using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Core.Settings;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Holds the analysis thresholds currently in force. Seeded from configuration and editable in
/// the Settings screen, so an operator can tune the rules without a redeploy.
/// </summary>
/// <remarks>
/// <see cref="Current"/> stays synchronous because every analysis page reads it while rendering;
/// the working copy is held in memory and the durable write happens on change. Mutations are
/// asynchronous so the save reaches the database before the confirmation is shown.
/// </remarks>
public interface ISettingsStore
{
    AuditSettings Current { get; }

    /// <summary>The values from configuration, used by "reset to defaults".</summary>
    AuditSettings ConfiguredDefaults { get; }

    /// <summary>True when a saved set was loaded rather than the configured defaults.</summary>
    bool IsPersisted { get; }

    Task UpdateAsync(
        AuditSettings settings, string? updatedBy = null, CancellationToken cancellationToken = default);

    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads any saved settings into the working copy. Called once at startup; a no-op where
    /// nothing is persisted.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory implementation. Used when persistence is switched off, and by the tests.
/// Configuration remains the source of truth across restarts.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly AuditSettings _defaults;
    private AuditSettings _current;

    public InMemorySettingsStore(IOptions<AuditorOptions> options)
    {
        _defaults = options.Value.Defaults;
        _current = _defaults;
    }

    public AuditSettings Current => _current;

    public AuditSettings ConfiguredDefaults => _defaults;

    public bool IsPersisted => false;

    public Task UpdateAsync(
        AuditSettings settings, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        _current = settings;
        return Task.CompletedTask;
    }

    public Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        _current = _defaults;
        return Task.CompletedTask;
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Keeps the thresholds in the database, so values entered in the Settings screen survive a
/// restart instead of reverting to configuration.
/// </summary>
/// <remarks>
/// Write-through rather than write-behind: the row is saved before the working copy changes, so a
/// failed save shows as an error rather than as a setting that silently reverts on the next
/// restart. Reset deletes the stored row rather than writing today's defaults into it, so a later
/// change to configuration is picked up.
/// </remarks>
public sealed class PersistentSettingsStore : ISettingsStore
{
    private readonly ISettingsRepository _repository;
    private readonly AuditSettings _defaults;
    private AuditSettings _current;

    public PersistentSettingsStore(IOptions<AuditorOptions> options, ISettingsRepository repository)
    {
        _repository = repository;
        _defaults = options.Value.Defaults;
        _current = _defaults;
    }

    public AuditSettings Current => _current;

    public AuditSettings ConfiguredDefaults => _defaults;

    public bool IsPersisted { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return;
        }

        _current = stored;
        IsPersisted = true;
    }

    public async Task UpdateAsync(
        AuditSettings settings, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        await _repository.SaveAsync(settings, updatedBy, cancellationToken).ConfigureAwait(false);

        _current = settings;
        IsPersisted = true;
    }

    public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await _repository.ClearAsync(cancellationToken).ConfigureAwait(false);

        _current = _defaults;
        IsPersisted = false;
    }
}
