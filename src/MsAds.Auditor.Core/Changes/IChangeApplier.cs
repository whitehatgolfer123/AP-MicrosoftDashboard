namespace MsAds.Auditor.Core.Changes;

/// <summary>
/// Applies approved changes to an advertising account.
/// </summary>
/// <remarks>
/// <para>
/// This is the only seam in the application that can alter a live account. It is deliberately
/// narrow, deliberately separate from <c>IAdsDataSource</c> (which remains read-only and has no
/// write method at all), and the default registration refuses every request.
/// </para>
/// <para>
/// Nothing calls this without an explicit, itemised confirmation from a person. There is no code
/// path that applies a recommendation automatically.
/// </para>
/// </remarks>
public interface IChangeApplier
{
    /// <summary>False when writes are switched off, which is the default.</summary>
    bool CanApply { get; }

    /// <summary>
    /// Why writes are unavailable, shown to the user. Empty when <see cref="CanApply"/> is true.
    /// </summary>
    string? DisabledReason { get; }

    /// <summary>True when the applier only simulates, as in demo mode.</summary>
    bool IsSimulated { get; }

    /// <summary>
    /// Applies the given changes.
    /// </summary>
    /// <param name="preview">
    /// When true nothing is sent; each change comes back as
    /// <see cref="ChangeStatus.Previewed"/> with the call that would have been made.
    /// </param>
    Task<ChangeRunResult> ApplyAsync(
        IReadOnlyList<ProposedChange> changes,
        string? requestedBy,
        bool preview,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The registration used whenever changes are not enabled. Refuses everything, so the read-only
/// default holds even if a caller reaches this far.
/// </summary>
public sealed class DisabledChangeApplier(string reason) : IChangeApplier
{
    public bool CanApply => false;

    public string? DisabledReason { get; } = reason;

    public bool IsSimulated => false;

    public Task<ChangeRunResult> ApplyAsync(
        IReadOnlyList<ProposedChange> changes,
        string? requestedBy,
        bool preview,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "Applying changes is disabled. " + DisabledReason);
}
