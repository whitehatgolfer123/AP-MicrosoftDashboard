using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Tests.Web;

/// <summary>
/// Who may open the application, and the guard that stops it being exposed anonymously.
/// </summary>
public class AppSignInTests
{
    [Fact]
    public void With_no_allow_list_anyone_in_the_directory_may_sign_in()
    {
        var options = new AppSignInOptions();

        Assert.True(options.IsUserAllowed("anyone@addpeople.co.uk", "anyone@addpeople.co.uk"));
    }

    [Fact]
    public void An_allow_list_admits_only_the_named_accounts()
    {
        var options = new AppSignInOptions { AllowedUsers = { "tom@addpeople.co.uk" } };

        Assert.True(options.IsUserAllowed("tom@addpeople.co.uk", null));
        Assert.False(options.IsUserAllowed("someone.else@addpeople.co.uk", null));
    }

    [Fact]
    public void The_allow_list_ignores_case_and_surrounding_space()
    {
        var options = new AppSignInOptions { AllowedUsers = { "  Tom@AddPeople.co.uk " } };

        Assert.True(options.IsUserAllowed("tom@addpeople.co.uk", null));
    }

    [Fact]
    public void A_match_on_either_the_principal_name_or_the_email_is_enough()
    {
        var options = new AppSignInOptions { AllowedUsers = { "tom@addpeople.co.uk" } };

        Assert.True(options.IsUserAllowed("someone-else", "tom@addpeople.co.uk"));
    }

    [Fact]
    public void Missing_settings_are_named_so_the_interface_can_say_what_is_needed()
    {
        var missing = new AppSignInOptions { ClientId = "set" }.MissingSettings();

        Assert.Contains("Directory (tenant) ID", missing);
        Assert.Contains("Client secret", missing);
        Assert.DoesNotContain("Application (client) ID", missing);
    }

    [Theory]
    [InlineData("http://localhost:5199", false)]
    [InlineData("https://localhost:7211", false)]
    [InlineData("http://127.0.0.1:5199", false)]
    [InlineData("http://[::1]:5199", false)]
    [InlineData("http://0.0.0.0:5199", true)]
    [InlineData("http://*:5199", true)]
    [InlineData("http://+:5199", true)]
    [InlineData("http://192.168.1.40:5199", true)]
    [InlineData("https://auditor.addpeople.local", true)]
    public void Addresses_are_classified_by_whether_other_machines_could_reach_them(
        string address, bool expectedReachable)
        => Assert.Equal(expectedReachable, NetworkExposureGuard.IsNetworkReachable(address));

    [Fact]
    public void Running_anonymously_on_localhost_is_allowed()
    {
        var exception = Record.Exception(() => NetworkExposureGuard.Validate(
            ["http://localhost:5199"], new AppSignInOptions { Enabled = false }));

        Assert.Null(exception);
    }

    [Fact]
    public void Running_anonymously_on_a_reachable_address_is_refused_with_an_explanation()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => NetworkExposureGuard.Validate(
            ["http://0.0.0.0:5199"], new AppSignInOptions { Enabled = false }));

        Assert.Contains("Refusing to start", exception.Message);
        Assert.Contains("Auditor:SignIn:Enabled", exception.Message);
    }

    [Fact]
    public void A_reachable_address_is_fine_once_sign_in_is_enabled()
    {
        var exception = Record.Exception(() => NetworkExposureGuard.Validate(
            ["http://0.0.0.0:5199"], new AppSignInOptions { Enabled = true }));

        Assert.Null(exception);
    }

    [Fact]
    public void The_guard_can_be_overridden_deliberately()
    {
        var exception = Record.Exception(() => NetworkExposureGuard.Validate(
            ["http://0.0.0.0:5199"],
            new AppSignInOptions { Enabled = false, AllowAnonymousAccessOnNetwork = true }));

        Assert.Null(exception);
    }

    [Fact]
    public void An_unparseable_address_is_treated_as_exposed_rather_than_assumed_safe()
        => Assert.True(NetworkExposureGuard.IsNetworkReachable("not a url"));

    [Fact]
    public void No_configured_address_means_the_server_default_applies_which_is_loopback()
    {
        var exception = Record.Exception(() => NetworkExposureGuard.Validate([], new AppSignInOptions()));

        Assert.Null(exception);
    }
}
