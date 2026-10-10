using MsAds.Auditor.Ai;
using MsAds.Auditor.Core.Ai;
using MsAds.Auditor.Core.Reports;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Routes each request to whichever narrator the current settings call for.
/// </summary>
/// <remarks>
/// The key can be added, changed or removed from the settings page without a restart, so the
/// choice between providers — and between a provider and nothing at all — has to be made per
/// call rather than once at startup.
/// </remarks>
public sealed class AiNarratorSwitch(
    IAiOptionsAccessor options,
    ClaudeNarrator claude,
    OpenAiNarrator openAi) : IAiNarrator
{
    private const string NotConfigured =
        "No AI API key is configured. Add an OpenAI or Anthropic key on the Settings page to "
        + "enable optional AI commentary; the audit works fully without it.";

    public bool IsConfigured => options.Current.IsConfigured;

    public string? DisabledReason => IsConfigured ? null : NotConfigured;

    public string Model => IsConfigured ? options.Current.EffectiveModel : "none";

    /// <summary>Which provider the commentary would be billed to, for display.</summary>
    public AiProvider Provider => options.Current.Provider;

    /// <summary>Rough per-report cost, so the spend is visible before anyone opts in.</summary>
    public decimal? TypicalCostUsd => AiPricing.TypicalReportCost(options.Current.EffectiveModel);

    public Task<AiNarrative?> NarrateAsync(
        AuditReport report, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Task.FromResult<AiNarrative?>(null);
        }

        IAiNarrator narrator = options.Current.Provider switch
        {
            AiProvider.OpenAi => openAi,
            _ => claude,
        };

        return narrator.NarrateAsync(report, cancellationToken);
    }
}
