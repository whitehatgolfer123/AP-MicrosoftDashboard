using MsAds.Auditor.Core.Demo;
using MsAds.Auditor.Core.Models;

namespace MsAds.Auditor.Tests.Demo;

public class AccountValidationTests
{
    private readonly DemoAccountDirectory _directory = new();

    [Fact]
    public async Task Accessible_accounts_are_listed()
    {
        var accounts = await _directory.ListAccessibleAccountsAsync();

        Assert.NotEmpty(accounts);
        Assert.All(accounts, a => Assert.StartsWith("Demo", a.AccountName));
    }

    [Fact]
    public async Task A_known_account_is_granted_and_carries_its_currency_and_time_zone()
    {
        var result = await _directory.ValidateAccountAsync(DemoAccounts.PrimaryAccountId);

        Assert.True(result.IsAccessible);
        Assert.NotNull(result.Account);
        Assert.Equal("GBP", result.Account!.CurrencyCode);
        Assert.Equal("Europe/London", result.Account.TimeZone);
    }

    [Fact]
    public async Task An_unknown_account_is_refused_with_a_message_the_user_can_act_on()
    {
        var result = await _directory.ValidateAccountAsync(999999);

        Assert.False(result.IsAccessible);
        Assert.Equal(AccountAccessFailure.NotFound, result.Failure);
        Assert.NotNull(result.Message);
        Assert.Contains("999999", result.Message);
    }

    [Fact]
    public void A_manager_id_and_an_account_id_are_kept_distinct()
    {
        var account = DemoAccounts.Primary;

        // The brief is explicit that these are not interchangeable.
        Assert.NotEqual(account.AccountId, account.CustomerId);
    }
}
