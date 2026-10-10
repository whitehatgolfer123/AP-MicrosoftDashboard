using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Models;

namespace MsAds.Auditor.Core.Demo;

/// <summary>
/// The accounts offered in demo mode. The names say "Demo" so a screenshot can never be mistaken
/// for a real client account.
/// </summary>
public static class DemoAccounts
{
    public const long PrimaryAccountId = 140100001;

    public static IReadOnlyList<AdAccountInfo> All { get; } =
    [
        new()
        {
            AccountId = PrimaryAccountId,
            AccountNumber = "X0001001",
            AccountName = "Demo — Gasfit Heating Ltd",
            CustomerId = 9900001,
            CustomerName = "Demo Manager Account",
            CurrencyCode = "GBP",
            TimeZone = "Europe/London",
            Status = "Active",
        },
        new()
        {
            AccountId = 140100002,
            AccountNumber = "X0001002",
            AccountName = "Demo — Northgate Legal",
            CustomerId = 9900001,
            CustomerName = "Demo Manager Account",
            CurrencyCode = "GBP",
            TimeZone = "Europe/London",
            Status = "Active",
        },
        new()
        {
            AccountId = 140100003,
            AccountNumber = "X0001003",
            AccountName = "Demo — Harbour Dental Care",
            CustomerId = 9900002,
            CustomerName = "Demo Manager Account",
            CurrencyCode = "GBP",
            TimeZone = "Europe/London",
            Status = "Active",
        },
        new()
        {
            AccountId = 140100004,
            AccountNumber = "X0001004",
            AccountName = "Demo — Pennine Roofing",
            CustomerId = 9900002,
            CustomerName = "Demo Manager Account",
            CurrencyCode = "GBP",
            TimeZone = "Europe/London",
            Status = "Active",
        },
        new()
        {
            AccountId = 140100005,
            AccountNumber = "X0001005",
            AccountName = "Demo — Calder Veterinary Group",
            CustomerId = 9900002,
            CustomerName = "Demo Manager Account",
            CurrencyCode = "GBP",
            TimeZone = "Europe/London",
            Status = "Active",
        },
        new()
        {
            AccountId = 140100006,
            AccountNumber = "X0001006",
            AccountName = "Demo — Westgate Opticians",
            CustomerId = 9900003,
            CustomerName = "Demo Manager Account",
            CurrencyCode = "GBP",
            TimeZone = "Europe/London",
            Status = "Active",
        },
        new()
        {
            AccountId = 140100007,
            AccountNumber = "X0001007",
            AccountName = "Demo — Riverside Removals",
            CustomerId = 9900003,
            CustomerName = "Demo Manager Account",
            CurrencyCode = "GBP",
            TimeZone = "Europe/London",
            Status = "Pause",
        },
        new()
        {
            AccountId = 140100008,
            AccountNumber = "X0001008",
            AccountName = "Demo — Lumen Electrical",
            CustomerId = 9900003,
            CustomerName = "Demo Manager Account",
            CurrencyCode = "GBP",
            TimeZone = "Europe/London",
            Status = "Active",
        },
    ];

    public static AdAccountInfo Primary => All[0];
}

/// <summary>
/// Account lookup for demo mode. Validates against the demo list so the account-access path can
/// be exercised, including the failure messages, without API credentials.
/// </summary>
public sealed class DemoAccountDirectory : IAccountDirectory
{
    public DataSourceKind Kind => DataSourceKind.Demo;

    public Task<IReadOnlyList<AdAccountInfo>> ListAccessibleAccountsAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(DemoAccounts.All);

    public Task<AccountAccessResult> ValidateAccountAsync(
        long accountId, CancellationToken cancellationToken = default)
    {
        var account = DemoAccounts.All.FirstOrDefault(a => a.AccountId == accountId);

        return Task.FromResult(account is null
            ? AccountAccessResult.Denied(
                AccountAccessFailure.NotFound,
                $"Account {accountId} is not one of the demo accounts. In demo mode only the listed demo accounts "
                + "can be opened; connect a Microsoft Advertising account to audit a real one.")
            : AccountAccessResult.Granted(account));
    }
}
