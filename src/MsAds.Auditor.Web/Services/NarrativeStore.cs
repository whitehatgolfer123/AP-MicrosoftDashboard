using System.Collections.Concurrent;
using MsAds.Auditor.Core.Ai;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Holds AI commentary against the report it describes.
/// </summary>
/// <remarks>
/// Kept beside the report rather than inside it, so the audit stays an immutable record of what
/// the deterministic engine produced. It also means a commentary is written once and then
/// re-displayed, rather than being regenerated — and re-billed — on every page view.
/// </remarks>
public interface INarrativeStore
{
    AiNarrative? Get(Guid reportId);

    void Set(Guid reportId, AiNarrative narrative);
}

public sealed class InMemoryNarrativeStore : INarrativeStore
{
    private readonly ConcurrentDictionary<Guid, AiNarrative> _narratives = new();

    public AiNarrative? Get(Guid reportId) => _narratives.GetValueOrDefault(reportId);

    public void Set(Guid reportId, AiNarrative narrative) => _narratives[reportId] = narrative;
}
