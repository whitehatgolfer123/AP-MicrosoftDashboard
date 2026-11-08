using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Data.Entities;

namespace MsAds.Auditor.Data.Mapping;

/// <summary>
/// Translates between the change-log domain records and their database rows.
/// </summary>
/// <remarks>
/// Kept as explicit, total mapping rather than a convention-based mapper so that adding a field
/// to <see cref="ProposedChange"/> and forgetting to persist it is a compile error, not a
/// silently truncated audit trail.
/// </remarks>
internal static class ChangeLogMapping
{
    internal static ChangeRunEntity ToEntity(ChangeRunResult result) => new()
    {
        RunId = result.RunId,
        RanAt = result.RanAt,
        RanBy = result.RanBy,
        AccountId = result.AccountId,
        AccountName = result.AccountName,
        WasPreview = result.WasPreview,
        Outcomes =
        [
            .. result.Outcomes.Select((outcome, index) => new ChangeOutcomeEntity
            {
                RunId = result.RunId,
                Ordinal = index,
                Status = outcome.Status,
                Message = outcome.Message,
                ChangeId = outcome.Change.Id,
                Kind = outcome.Change.Kind,
                Risk = outcome.Change.Risk,
                ChangeAccountId = outcome.Change.AccountId,
                CustomerId = outcome.Change.CustomerId,
                TargetEntityId = outcome.Change.TargetEntityId,
                TargetDescription = outcome.Change.TargetDescription,
                Value = outcome.Change.Value,
                MatchType = outcome.Change.MatchType,
                Summary = outcome.Change.Summary,
                Rationale = outcome.Change.Rationale,
                Source = outcome.Change.Source,
                SpendAtStake = outcome.Change.SpendAtStake,
                Caveats = [.. outcome.Change.Caveats],
            }),
        ],
    };

    internal static ChangeRunResult ToDomain(ChangeRunEntity entity) => new()
    {
        RunId = entity.RunId,
        RanAt = entity.RanAt,
        RanBy = entity.RanBy,
        AccountId = entity.AccountId,
        AccountName = entity.AccountName,
        WasPreview = entity.WasPreview,
        Outcomes =
        [
            .. entity.Outcomes
                .OrderBy(o => o.Ordinal)
                .Select(o => new ChangeOutcome(ToDomain(o), o.Status, o.Message)),
        ],
    };

    private static ProposedChange ToDomain(ChangeOutcomeEntity entity) => new()
    {
        Id = entity.ChangeId,
        Kind = entity.Kind,
        Risk = entity.Risk,
        AccountId = entity.ChangeAccountId,
        CustomerId = entity.CustomerId,
        TargetEntityId = entity.TargetEntityId,
        TargetDescription = entity.TargetDescription,
        Value = entity.Value,
        MatchType = entity.MatchType,
        Summary = entity.Summary,
        Rationale = entity.Rationale,
        Source = entity.Source,
        SpendAtStake = entity.SpendAtStake,
        Caveats = [.. entity.Caveats],
    };
}
