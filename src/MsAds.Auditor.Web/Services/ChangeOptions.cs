using MsAds.Auditor.Core.Changes;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Whether the application may alter advertising accounts, and how far.
/// </summary>
/// <remarks>
/// Off by default. The application is read-only unless someone deliberately turns this on, and
/// the two risk levels are gated separately: adding an exclusion and switching off a campaign
/// are different decisions and should not be enabled by the same flag.
/// </remarks>
public sealed class ChangeOptions
{
    public const string SectionName = "Auditor:Changes";

    /// <summary>
    /// Master switch. While false the application cannot write to an account at all, and the
    /// applier registered in its place refuses every request.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Also permit changes that stop something delivering — pausing a keyword or a campaign.
    /// Separate from <see cref="Enabled"/> so exclusions can be allowed without it.
    /// </summary>
    public bool AllowPausing { get; set; }

    /// <summary>
    /// Upper bound on one apply run, as a guard against a mis-click selecting everything in a
    /// large account.
    /// </summary>
    public int MaxChangesPerRun { get; set; } = 50;

    /// <summary>Whether a particular proposal is permitted by the current configuration.</summary>
    public bool Permits(ProposedChange change) =>
        Enabled && (change.Risk != ChangeRisk.StopsDelivery || AllowPausing);

    /// <summary>Why a proposal is not permitted, for display next to it.</summary>
    public string? RefusalReason(ProposedChange change)
    {
        if (!Enabled)
        {
            return "Applying changes is switched off.";
        }

        return change.Risk == ChangeRisk.StopsDelivery && !AllowPausing
            ? "Changes that stop delivery are switched off."
            : null;
    }
}
