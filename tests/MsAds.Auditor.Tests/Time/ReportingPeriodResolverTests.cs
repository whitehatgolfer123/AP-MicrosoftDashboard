using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Tests.Time;

public class ReportingPeriodResolverTests
{
    /// <summary>Monday 16 March 2026, 10:30 UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 16, 10, 30, 0, TimeSpan.Zero);

    private static ReportingPeriodResolver Resolver(DateTimeOffset? now = null)
        => new(new FixedClock(now ?? Now));

    [Theory]
    [InlineData(ReportingPeriod.Last7Days, 7)]
    [InlineData(ReportingPeriod.Last14Days, 14)]
    [InlineData(ReportingPeriod.Last30Days, 30)]
    public void The_current_period_spans_the_requested_number_of_days(ReportingPeriod period, int days)
    {
        var resolved = Resolver().Resolve(period, TimeZoneInfo.Utc);

        Assert.Equal(days, resolved.Current.Days);
        Assert.Equal(days, resolved.Previous!.Value.Days);
    }

    [Fact]
    public void Today_is_excluded_by_default_because_its_data_is_still_accumulating()
    {
        var resolved = Resolver().Resolve(ReportingPeriod.Last7Days, TimeZoneInfo.Utc);

        Assert.True(resolved.CurrentDayExcluded);
        Assert.Equal(new DateOnly(2026, 3, 15), resolved.Current.End);
        Assert.Equal(new DateOnly(2026, 3, 9), resolved.Current.Start);
        Assert.Equal(new DateOnly(2026, 3, 16), resolved.AccountToday);
    }

    [Fact]
    public void Today_can_be_included_when_the_user_asks_for_it()
    {
        var resolved = Resolver().Resolve(ReportingPeriod.Last7Days, TimeZoneInfo.Utc, excludeCurrentDay: false);

        Assert.False(resolved.CurrentDayExcluded);
        Assert.Equal(new DateOnly(2026, 3, 16), resolved.Current.End);
    }

    [Fact]
    public void The_comparison_period_is_the_equally_long_window_immediately_before_and_does_not_overlap()
    {
        var resolved = Resolver().Resolve(ReportingPeriod.Last7Days, TimeZoneInfo.Utc);
        var previous = resolved.Previous!.Value;

        Assert.Equal(new DateOnly(2026, 3, 8), previous.End);
        Assert.Equal(new DateOnly(2026, 3, 2), previous.Start);
        Assert.Equal(resolved.Current.Start.AddDays(-1), previous.End);
        Assert.Equal(resolved.Current.Days, previous.Days);
    }

    [Fact]
    public void The_comparison_can_be_omitted()
    {
        var resolved = Resolver().Resolve(ReportingPeriod.Last30Days, TimeZoneInfo.Utc, includeComparison: false);

        Assert.Null(resolved.Previous);
        Assert.False(resolved.HasComparison);
    }

    [Fact]
    public void Dates_are_decided_in_the_accounts_time_zone_not_the_servers()
    {
        // 16 March 10:30 UTC is still 15 March in Los Angeles, so "yesterday" differs by a day.
        var losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var resolved = Resolver(new DateTimeOffset(2026, 3, 16, 3, 0, 0, TimeSpan.Zero))
            .Resolve(ReportingPeriod.Last7Days, losAngeles);

        Assert.Equal(new DateOnly(2026, 3, 15), resolved.AccountToday);
        Assert.Equal(new DateOnly(2026, 3, 14), resolved.Current.End);
        Assert.Equal("America/Los_Angeles", resolved.TimeZoneId);
    }

    [Fact]
    public void An_unknown_time_zone_falls_back_to_utc_and_records_that_it_did()
    {
        var resolved = Resolver().Resolve(ReportingPeriod.Last7Days, accountTimeZone: null);

        Assert.True(resolved.TimeZoneFellBackToUtc);
        Assert.Equal("UTC", resolved.TimeZoneId);
    }
}
