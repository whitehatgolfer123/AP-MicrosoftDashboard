using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Tests.Time;

public class DateRangeTests
{
    [Fact]
    public void The_range_is_inclusive_of_both_endpoints()
        => Assert.Equal(7, new DateRange(new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15)).Days);

    [Fact]
    public void A_single_day_range_spans_one_day()
        => Assert.Equal(1, new DateRange(new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 9)).Days);

    [Fact]
    public void An_end_before_the_start_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new DateRange(new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 9)));

    [Fact]
    public void EachDay_enumerates_every_date_once()
    {
        var days = new DateRange(new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 11)).EachDay().ToList();

        Assert.Equal(3, days.Count);
        Assert.Equal(new DateOnly(2026, 3, 9), days[0]);
        Assert.Equal(new DateOnly(2026, 3, 11), days[2]);
    }

    [Fact]
    public void The_preceding_period_abuts_the_range_without_overlapping_it()
    {
        var range = new DateRange(new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 15));
        var preceding = range.PrecedingPeriod();

        Assert.Equal(new DateOnly(2026, 3, 2), preceding.Start);
        Assert.Equal(new DateOnly(2026, 3, 8), preceding.End);
        Assert.Equal(range.Days, preceding.Days);
        Assert.False(preceding.Contains(range.Start));
    }

    [Fact]
    public void The_preceding_period_handles_a_month_boundary()
    {
        var preceding = new DateRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 30)).PrecedingPeriod();

        Assert.Equal(new DateOnly(2026, 1, 30), preceding.Start);
        Assert.Equal(new DateOnly(2026, 2, 28), preceding.End);
        Assert.Equal(30, preceding.Days);
    }
}
