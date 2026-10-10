using Microsoft.Extensions.Logging;
using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.Demo;

/// <summary>
/// Simulates applying changes, so the whole review-and-approve workflow can be exercised in demo
/// mode without an advertising account existing.
/// </summary>
/// <remarks>
/// Nothing leaves the process. Every outcome is reported as simulated, and the interface labels
/// it as such, so a demo run can never be mistaken for a real one.
/// </remarks>
public sealed class DemoChangeApplier(IClock clock, ILogger<DemoChangeApplier> logger) : IChangeApplier
{
    public bool CanApply => true;

    public string? DisabledReason => null;

    public bool IsSimulated => true;

    public Task<ChangeRunResult> ApplyAsync(
        IReadOnlyList<ProposedChange> changes,
        string? requestedBy,
        bool preview,
        CancellationToken cancellationToken = default)
    {
        var outcomes = changes.Select(change => new ChangeOutcome(
            change,
            preview ? ChangeStatus.Previewed : ChangeStatus.Applied,
            preview
                ? $"Would call {ChangeOperation.Describe(change)} — nothing was sent."
                : $"Simulated: {ChangeOperation.Describe(change)} was not sent to any account, "
                  + "because this is demo data."))
            .ToList();

        logger.LogInformation(
            "Demo change run: {Count} change(s), preview={Preview}", changes.Count, preview);

        return Task.FromResult(new ChangeRunResult
        {
            RunId = Guid.NewGuid(),
            RanAt = clock.UtcNow,
            RanBy = requestedBy,
            AccountId = changes.FirstOrDefault()?.AccountId ?? 0,
            AccountName = DemoAccounts.All
                .FirstOrDefault(a => a.AccountId == (changes.FirstOrDefault()?.AccountId ?? 0))?.AccountName
                ?? "Demo account",
            WasPreview = preview,
            Outcomes = outcomes,
        });
    }
}
