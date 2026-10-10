namespace MsAds.Auditor.Core.Ai;

/// <summary>
/// An optional, AI-written commentary on a finished audit.
/// </summary>
/// <remarks>
/// Strictly additive. The deterministic executive summary and every calculated figure remain
/// the authoritative output; this sits alongside them and may never replace or contradict them.
/// If it is unavailable the report is complete without it.
/// </remarks>
public sealed record AiNarrative
{
    public required string Text { get; init; }

    /// <summary>The model that produced it, shown so the reader knows what wrote this.</summary>
    public required string Model { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    /// <summary>
    /// What this one call cost, in US dollars. Shown because the person running it is paying
    /// per report and should see that rather than discover it on a bill.
    /// </summary>
    public decimal? EstimatedCostUsd { get; init; }

    public TimeSpan Duration { get; init; }

    public static AiNarrative Unavailable(string reason) => new()
    {
        Text = reason,
        Model = "none",
        Failed = true,
    };

    public bool Failed { get; init; }
}

/// <summary>
/// Writes an optional narrative for a report.
/// </summary>
/// <remarks>
/// Implementations must treat the supplied figures as the only source of fact. A narrator that
/// cannot run returns <see langword="null"/> rather than throwing: a missing narrative must
/// never cost the user their audit.
/// </remarks>
public interface IAiNarrator
{
    /// <summary>False when no API key is configured, which is the default.</summary>
    bool IsConfigured { get; }

    /// <summary>Why it is unavailable, for display. Null when configured.</summary>
    string? DisabledReason { get; }

    /// <summary>The model that would be used, for display before anyone spends anything.</summary>
    string Model { get; }

    /// <summary>
    /// Produces a narrative, or <see langword="null"/> when unavailable. Never throws for an
    /// API failure — the audit stands without it.
    /// </summary>
    Task<AiNarrative?> NarrateAsync(
        Reports.AuditReport report, CancellationToken cancellationToken = default);
}

/// <summary>The narrator used whenever no API key is configured. Does nothing, costs nothing.</summary>
public sealed class DisabledAiNarrator(string reason) : IAiNarrator
{
    public bool IsConfigured => false;

    public string? DisabledReason { get; } = reason;

    public string Model => "none";

    public Task<AiNarrative?> NarrateAsync(
        Reports.AuditReport report, CancellationToken cancellationToken = default)
        => Task.FromResult<AiNarrative?>(null);
}
