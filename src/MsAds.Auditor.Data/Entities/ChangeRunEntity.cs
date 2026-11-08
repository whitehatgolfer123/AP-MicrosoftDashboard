using MsAds.Auditor.Core.Changes;

namespace MsAds.Auditor.Data.Entities;

/// <summary>
/// One recorded apply run. The database shape of <see cref="ChangeRunResult"/>.
/// </summary>
/// <remarks>
/// A separate type from the domain record on purpose. <see cref="ChangeRunResult"/> is an
/// immutable record with computed properties and a nested <see cref="ProposedChange"/>; mapping
/// EF directly onto it would force the domain to carry keys and navigation properties it has no
/// use for. The translation lives in <c>ChangeLogMapping</c>.
/// </remarks>
internal sealed class ChangeRunEntity
{
    public Guid RunId { get; set; }

    public DateTimeOffset RanAt { get; set; }

    /// <summary>Who ran it. Null when sign-in is not enabled. Never a credential.</summary>
    public string? RanBy { get; set; }

    public long AccountId { get; set; }

    public string AccountName { get; set; } = string.Empty;

    /// <summary>True when nothing was sent and the outcomes are a preview.</summary>
    public bool WasPreview { get; set; }

    public List<ChangeOutcomeEntity> Outcomes { get; set; } = [];
}

/// <summary>
/// What happened to one proposed change within a run, with the proposal flattened alongside it.
/// </summary>
/// <remarks>
/// The proposal is stored rather than referenced. A proposal is derived from a report that is
/// never persisted, so by the time anyone reads this row the report is gone — the audit trail
/// has to carry its own copy of what was proposed, or it cannot answer what was changed.
/// </remarks>
internal sealed class ChangeOutcomeEntity
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public ChangeRunEntity? Run { get; set; }

    /// <summary>Position within the run, so outcomes read back in the order they were applied.</summary>
    public int Ordinal { get; set; }

    public ChangeStatus Status { get; set; }

    public string Message { get; set; } = string.Empty;

    // --- the proposal, flattened -------------------------------------------------------------

    /// <summary>
    /// The proposal's own id, stable within a report. Not the primary key: the same proposal can
    /// legitimately appear in a preview run and then an apply run.
    /// </summary>
    public string ChangeId { get; set; } = string.Empty;

    public ChangeActionKind Kind { get; set; }

    public ChangeRisk Risk { get; set; }

    public long ChangeAccountId { get; set; }

    public long CustomerId { get; set; }

    public long TargetEntityId { get; set; }

    public string TargetDescription { get; set; } = string.Empty;

    /// <summary>The negative keyword text, for the exclusion actions.</summary>
    public string? Value { get; set; }

    public string? MatchType { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string Rationale { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public decimal? SpendAtStake { get; set; }

    /// <summary>Stored as a JSON array; see the converter in <c>AuditorDbContext</c>.</summary>
    public List<string> Caveats { get; set; } = [];
}
