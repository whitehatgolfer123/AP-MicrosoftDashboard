using Microsoft.BingAds;
using Microsoft.BingAds.V13.CampaignManagement;
using Microsoft.Extensions.Logging;
using MsAds.Auditor.BingAds.Authentication;
using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.BingAds.CampaignManagement;

/// <summary>
/// Applies approved changes through the Microsoft Advertising Campaign Management service.
/// </summary>
/// <remarks>
/// <para>
/// The only type in the application that writes to a live advertising account. It is reached
/// only from an explicit, itemised confirmation by a person; nothing calls it automatically.
/// </para>
/// <para>
/// Changes are sent one at a time rather than batched. It is slower, but it means a single
/// rejected change cannot take the rest of the batch with it, and the audit log records exactly
/// which ones landed.
/// </para>
/// </remarks>
public sealed class BingAdsChangeApplier(
    BingAdsAuthenticationService authentication,
    IClock clock,
    ILogger<BingAdsChangeApplier> logger) : IChangeApplier
{
    public bool CanApply => true;

    public string? DisabledReason => null;

    public bool IsSimulated => false;

    public async Task<ChangeRunResult> ApplyAsync(
        IReadOnlyList<ProposedChange> changes,
        string? requestedBy,
        bool preview,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<ChangeOutcome>();
        var accountId = changes.FirstOrDefault()?.AccountId ?? 0;
        var customerId = changes.FirstOrDefault()?.CustomerId ?? 0;

        if (preview)
        {
            // Nothing is sent. The user sees the exact call each change would make.
            return Result(
                changes.Select(c => new ChangeOutcome(
                    c, ChangeStatus.Previewed, $"Would call {ChangeOperation.Describe(c)}. Nothing was sent.")).ToList(),
                accountId, requestedBy, preview: true);
        }

        var authorization = await authentication
            .GetAuthorizationDataAsync(accountId, customerId, cancellationToken)
            .ConfigureAwait(false);

        using var service = new ServiceClient<ICampaignManagementService>(
            authorization, authentication.Environment);

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var message = await ApplyOneAsync(service, change, cancellationToken).ConfigureAwait(false);
                outcomes.Add(new ChangeOutcome(change, ChangeStatus.Applied, message));

                // Deliberately logged: this is the audit trail of what the tool altered.
                logger.LogInformation(
                    "Applied change {ChangeId} to account {AccountId}: {Summary}",
                    change.Id, change.AccountId, change.Summary);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Change {ChangeId} failed for account {AccountId}", change.Id, change.AccountId);

                outcomes.Add(new ChangeOutcome(
                    change, ChangeStatus.Failed,
                    $"Microsoft Advertising rejected this change: {Summarise(ex)}"));
            }
        }

        return Result(outcomes, accountId, requestedBy, preview: false);
    }

    private static async Task<string> ApplyOneAsync(
        ServiceClient<ICampaignManagementService> service,
        ProposedChange change,
        CancellationToken cancellationToken)
    {
        switch (change.Kind)
        {
            case ChangeActionKind.AddCampaignNegativeKeyword:
            case ChangeActionKind.AddAdGroupNegativeKeyword:
            {
                var entityType = change.Kind == ChangeActionKind.AddCampaignNegativeKeyword
                    ? "Campaign"
                    : "AdGroup";

                var response = await service.CallAsync(
                    (s, r) => s.AddNegativeKeywordsToEntitiesAsync(r),
                    new AddNegativeKeywordsToEntitiesRequest
                    {
                        EntityNegativeKeywords =
                        [
                            new EntityNegativeKeyword
                            {
                                EntityId = change.TargetEntityId,
                                EntityType = entityType,
                                NegativeKeywords =
                                [
                                    new NegativeKeyword
                                    {
                                        Text = change.Value,
                                        MatchType = ParseMatchType(change.MatchType),
                                    },
                                ],
                            },
                        ],
                    }).ConfigureAwait(false);

                ThrowOnNestedErrors(response.NestedPartialErrors);

                return $"Added “{change.Value}” as a {change.MatchType?.ToLowerInvariant()} negative keyword "
                       + $"on {entityType.ToLowerInvariant()} {change.TargetEntityId}.";
            }

            case ChangeActionKind.PauseKeyword:
            {
                var response = await service.CallAsync(
                    (s, r) => s.UpdateKeywordsAsync(r),
                    new UpdateKeywordsRequest
                    {
                        AdGroupId = 0,
                        Keywords = [new Keyword { Id = change.TargetEntityId, Status = KeywordStatus.Paused }],
                    }).ConfigureAwait(false);

                ThrowOnErrors(response.PartialErrors);

                return $"Paused keyword {change.TargetEntityId}.";
            }

            case ChangeActionKind.PauseCampaign:
            {
                var response = await service.CallAsync(
                    (s, r) => s.UpdateCampaignsAsync(r),
                    new UpdateCampaignsRequest
                    {
                        AccountId = change.AccountId,
                        Campaigns = [new Campaign { Id = change.TargetEntityId, Status = CampaignStatus.Paused }],
                    }).ConfigureAwait(false);

                ThrowOnErrors(response.PartialErrors);

                return $"Paused campaign {change.TargetEntityId}.";
            }

            default:
                throw new NotSupportedException($"Change kind {change.Kind} is not supported.");
        }
    }

    private static Microsoft.BingAds.V13.CampaignManagement.MatchType ParseMatchType(string? matchType)
        => Enum.TryParse<Microsoft.BingAds.V13.CampaignManagement.MatchType>(
            matchType, ignoreCase: true, out var parsed)
            ? parsed
            : Microsoft.BingAds.V13.CampaignManagement.MatchType.Exact;

    /// <summary>
    /// The service reports per-item rejections in a partial-errors collection rather than by
    /// failing the call, so a change is only genuinely applied when that collection is empty.
    /// </summary>
    private static void ThrowOnErrors(IList<BatchError>? errors)
    {
        if (errors is { Count: > 0 })
        {
            throw new InvalidOperationException(
                string.Join("; ", errors.Select(e => $"{e.ErrorCode}: {e.Message}")));
        }
    }

    private static void ThrowOnNestedErrors(IList<BatchErrorCollection>? errors)
    {
        var flattened = errors?
            .SelectMany(collection => collection.BatchErrors ?? [])
            .ToList();

        if (flattened is { Count: > 0 })
        {
            throw new InvalidOperationException(
                string.Join("; ", flattened.Select(e => $"{e.ErrorCode}: {e.Message}")));
        }
    }

    private static string Summarise(Exception ex) => ex switch
    {
        InvalidOperationException => ex.Message,
        _ => "an unexpected error occurred; the detail is in the application log.",
    };

    private ChangeRunResult Result(
        IReadOnlyList<ChangeOutcome> outcomes, long accountId, string? requestedBy, bool preview) => new()
    {
        RunId = Guid.NewGuid(),
        RanAt = clock.UtcNow,
        RanBy = requestedBy,
        AccountId = accountId,
        AccountName = $"Account {accountId}",
        WasPreview = preview,
        Outcomes = outcomes,
    };
}
