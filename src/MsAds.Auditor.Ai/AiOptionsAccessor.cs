using Microsoft.Extensions.Options;

namespace MsAds.Auditor.Ai;

/// <summary>
/// Supplies the AI settings currently in force.
/// </summary>
/// <remarks>
/// Deliberately not <c>IOptions&lt;AiOptions&gt;</c>. The key can be added, changed or removed from
/// the settings page while the application is running, so the narrators must read the current
/// values on every call rather than capturing whatever was in configuration at startup.
/// </remarks>
public interface IAiOptionsAccessor
{
    AiOptions Current { get; }
}

/// <summary>Reads the settings from configuration only. Used when nothing can change at runtime.</summary>
public sealed class StaticAiOptionsAccessor(IOptions<AiOptions> options) : IAiOptionsAccessor
{
    public AiOptions Current => options.Value;
}
