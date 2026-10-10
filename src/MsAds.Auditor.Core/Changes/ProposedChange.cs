namespace MsAds.Auditor.Core.Changes;

/// <summary>The kinds of change the application can make. Deliberately a short list.</summary>
public enum ChangeActionKind
{
    /// <summary>Exclude a query across a whole campaign.</summary>
    AddCampaignNegativeKeyword,

    /// <summary>Exclude a query within one ad group.</summary>
    AddAdGroupNegativeKeyword,

    /// <summary>Stop a keyword serving.</summary>
    PauseKeyword,

    /// <summary>Stop a campaign serving.</summary>
    PauseCampaign,
}

/// <summary>
/// How consequential a change is. Shown against every proposal, and used to gate the two kinds
/// behind separate switches, because adding an exclusion and switching off a campaign are not
/// the same decision.
/// </summary>
public enum ChangeRisk
{
    /// <summary>Adds an exclusion. Reversible by deleting it, but it blocks future traffic.</summary>
    Additive,

    /// <summary>Stops something delivering. Reversible, but it takes spend to zero meanwhile.</summary>
    StopsDelivery,
}

/// <summary>
/// One change the user could choose to apply, carrying everything needed to review it: what it
/// does, why it was proposed, and what to be careful of.
/// </summary>
public sealed record ProposedChange
{
    /// <summary>
    /// Stable within a report, so a selection survives the review step. Derived from the target
    /// and the value rather than generated, so the same proposal keeps the same id.
    /// </summary>
    public required string Id { get; init; }

    public required ChangeActionKind Kind { get; init; }

    public required ChangeRisk Risk { get; init; }

    public required long AccountId { get; init; }

    /// <summary>The customer that owns the account — the client's, not the agency's.</summary>
    public required long CustomerId { get; init; }

    /// <summary>The campaign or ad group the exclusion attaches to, or the entity to pause.</summary>
    public required long TargetEntityId { get; init; }

    /// <summary>Where the change lands, for display, e.g. "Campaign: Generic – Broad discovery".</summary>
    public required string TargetDescription { get; init; }

    /// <summary>The negative keyword text, for the exclusion actions.</summary>
    public string? Value { get; init; }

    /// <summary>Match type for the exclusion, e.g. Phrase.</summary>
    public string? MatchType { get; init; }

    /// <summary>One line stating exactly what will happen.</summary>
    public required string Summary { get; init; }

    /// <summary>Why it is being proposed, in terms of the account's own figures.</summary>
    public required string Rationale { get; init; }

    /// <summary>The rule or analysis this came from, for traceability.</summary>
    public required string Source { get; init; }

    /// <summary>Spend associated with the finding, used only for ordering.</summary>
    public decimal? SpendAtStake { get; init; }

    public IReadOnlyList<string> Caveats { get; init; } = [];
}

/// <summary>
/// Names the API operation a change maps to, so a preview can state exactly what would be sent.
/// Shared by every applier, so the preview and the real call can never describe it differently.
/// </summary>
public static class ChangeOperation
{
    public static string Describe(ProposedChange change) => change.Kind switch
    {
        ChangeActionKind.AddCampaignNegativeKeyword =>
            $"AddNegativeKeywordsToEntities(Campaign {change.TargetEntityId}, \u201C{change.Value}\u201D {change.MatchType})",
        ChangeActionKind.AddAdGroupNegativeKeyword =>
            $"AddNegativeKeywordsToEntities(AdGroup {change.TargetEntityId}, \u201C{change.Value}\u201D {change.MatchType})",
        ChangeActionKind.PauseKeyword =>
            $"UpdateKeywords(Keyword {change.TargetEntityId} \u2192 Paused)",
        ChangeActionKind.PauseCampaign =>
            $"UpdateCampaigns(Campaign {change.TargetEntityId} \u2192 Paused)",
        _ => change.Summary,
    };
}

public enum ChangeStatus
{
    /// <summary>Not sent, because the run was a preview.</summary>
    Previewed,

    Applied,

    Failed,

    /// <summary>Not sent, because the applier refused it.</summary>
    Skipped,
}

/// <summary>What happened to one proposed change.</summary>
public sealed record ChangeOutcome(ProposedChange Change, ChangeStatus Status, string Message);

/// <summary>The record of one apply run, kept so there is an audit trail of what was altered.</summary>
public sealed record ChangeRunResult
{
    public required Guid RunId { get; init; }

    public required DateTimeOffset RanAt { get; init; }

    /// <summary>Who ran it, where sign-in is enabled. Never a credential.</summary>
    public string? RanBy { get; init; }

    public required long AccountId { get; init; }

    public required string AccountName { get; init; }

    /// <summary>True when nothing was sent and the outcomes are a preview.</summary>
    public required bool WasPreview { get; init; }

    public IReadOnlyList<ChangeOutcome> Outcomes { get; init; } = [];

    public int AppliedCount => Outcomes.Count(o => o.Status == ChangeStatus.Applied);

    public int FailedCount => Outcomes.Count(o => o.Status == ChangeStatus.Failed);

    public int SkippedCount => Outcomes.Count(o => o.Status == ChangeStatus.Skipped);
}
