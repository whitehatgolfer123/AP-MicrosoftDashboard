using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Settings;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Holds the analysis thresholds currently in force. Seeded from configuration and editable in
/// the Settings screen, so an operator can tune the rules without a redeploy.
/// </summary>
public interface ISettingsStore
{
    AuditSettings Current { get; }

    /// <summary>The values from configuration, used by "reset to defaults".</summary>
    AuditSettings ConfiguredDefaults { get; }

    void Update(AuditSettings settings);

    void ResetToDefaults();
}

/// <summary>
/// In-memory implementation. Not persisted: these are operator preferences rather than data, and
/// keeping them in memory avoids writing anything to disk. Configuration remains the source of
/// truth across restarts.
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

    public void Update(AuditSettings settings) => _current = settings;

    public void ResetToDefaults() => _current = _defaults;
}
