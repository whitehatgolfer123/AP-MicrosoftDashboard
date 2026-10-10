using Microsoft.BingAds;
using Microsoft.BingAds.V13.CustomerManagement;
using Microsoft.Extensions.Logging;
using MsAds.Auditor.BingAds.Authentication;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Models;

namespace MsAds.Auditor.BingAds;

/// <summary>
/// Lists and validates the advertising accounts the connected user can reach, through the
/// Customer Management service.
/// </summary>
/// <remarks>
/// <para>
/// For an agency the useful property is that a developer token grants no permissions of its own:
/// the accounts returned here are exactly the accounts the signed-in user already sees in the
/// Microsoft Advertising interface, whether owned directly or managed through a manager account.
/// </para>
/// <para>
/// Each account carries its own <see cref="AdAccountInfo.CustomerId"/> taken from
/// <c>ParentCustomerId</c>. That matters when managing other customers' accounts: API calls must
/// carry the customer that owns the account, not the manager account's own customer id.
/// </para>
/// </remarks>
public sealed class BingAdsAccountDirectory(
    BingAdsAuthenticationService authentication,
    ILogger<BingAdsAccountDirectory> logger) : IAccountDirectory
{
    /// <summary>Accounts are fetched in pages; the service caps the page size.</summary>
    private const int PageSize = 100;

    /// <summary>How long a listing is reused before being fetched again.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<AdAccountInfo>? _cached;
    private DateTimeOffset _cachedAt;

    public DataSourceKind Kind => DataSourceKind.Live;

    public async Task<IReadOnlyList<AdAccountInfo>> ListAccessibleAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
            {
                return _cached;
            }

            var authorization = await authentication
                .GetAuthorizationDataAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            using var service = new ServiceClient<ICustomerManagementService>(
                authorization, authentication.Environment);

            // Who is signed in? The user id scopes the account search.
            var user = (await service
                .CallAsync((s, r) => s.GetUserAsync(r), new GetUserRequest { UserId = null })
                .ConfigureAwait(false)).User;

            authentication.RecordSignedInUser(user.UserName);

            var accounts = new List<AdAccountInfo>();
            var page = 0;

            while (true)
            {
                var response = await service.CallAsync(
                    (s, r) => s.SearchAccountsAsync(r),
                    new SearchAccountsRequest
                    {
                        Predicates =
                        [
                            new Predicate
                            {
                                Field = "UserId",
                                Operator = PredicateOperator.Equals,
                                Value = user.Id.ToString(),
                            },
                        ],
                        PageInfo = new Paging { Index = page, Size = PageSize },
                    }).ConfigureAwait(false);

                var batch = response.Accounts;

                if (batch is null || batch.Count == 0)
                {
                    break;
                }

                accounts.AddRange(batch.Select(Map));

                if (batch.Count < PageSize)
                {
                    break;
                }

                page++;
            }

            _cached = [.. accounts.OrderBy(a => a.AccountName, StringComparer.CurrentCultureIgnoreCase)];
            _cachedAt = DateTimeOffset.UtcNow;

            logger.LogInformation(
                "Retrieved {Count} accessible Microsoft Advertising account(s) for the connected user.",
                _cached.Count);

            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccountAccessResult> ValidateAccountAsync(
        long accountId, CancellationToken cancellationToken = default)
    {
        if (accountId <= 0)
        {
            return AccountAccessResult.Denied(
                AccountAccessFailure.InvalidFormat,
                "Account IDs are positive numbers.");
        }

        try
        {
            var accounts = await ListAccessibleAccountsAsync(cancellationToken).ConfigureAwait(false);
            var match = accounts.FirstOrDefault(a => a.AccountId == accountId);

            if (match is not null)
            {
                return AccountAccessResult.Granted(match);
            }

            // A manager id entered in place of an account id is the usual mistake, so name it.
            var managerMatch = accounts.FirstOrDefault(a => a.CustomerId == accountId);

            return AccountAccessResult.Denied(
                AccountAccessFailure.NotFound,
                managerMatch is not null
                    ? $"{accountId} is a manager (customer) ID, not an advertising account ID. The two are "
                      + $"different identifiers. Accounts under that manager include "
                      + $"{managerMatch.AccountName} ({managerMatch.AccountId})."
                    : $"Account {accountId} is not among the {accounts.Count} account(s) the signed-in user "
                      + "can access. Check the ID, and check that the user has been granted access to this "
                      + "account in Microsoft Advertising.");
        }
        catch (BingAdsNotConnectedException ex)
        {
            return AccountAccessResult.Denied(AccountAccessFailure.NotAuthenticated, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Account validation failed for account {AccountId}", accountId);

            return AccountAccessResult.Denied(
                AccountAccessFailure.ServiceError,
                "Microsoft Advertising could not be reached to verify this account. This is usually "
                + "temporary.");
        }
    }

    /// <summary>Clears the cached listing, so the next request re-reads it from the API.</summary>
    public void InvalidateCache()
    {
        _cached = null;
    }

    private static AdAccountInfo Map(AdvertiserAccount account) => new()
    {
        AccountId = account.Id ?? 0,
        AccountNumber = account.Number,
        AccountName = account.Name ?? $"Account {account.Id}",
        // The owning customer, which for a managed account is the client's, not the agency's.
        CustomerId = account.ParentCustomerId,
        CurrencyCode = account.CurrencyCode?.ToString(),
        TimeZone = account.TimeZone?.ToString(),
        Status = account.AccountLifeCycleStatus?.ToString(),
    };
}
