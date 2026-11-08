using MsAds.Auditor.Core.Scheduling;

namespace MsAds.Auditor.Tests.Scheduling;

/// <summary>
/// The schedule arithmetic behind a scheduled portfolio scan.
/// </summary>
/// <remarks>
/// Tested thoroughly because every awkward case lives here: a time that has already passed
/// today, a day list that wraps into next week, and the two days a year when a local time either
/// does not exist or happens twice.
/// </remarks>
public class ScanScheduleTests
{
    /// <summary>
    /// Europe/London, so the clock-change cases are real rather than contrived. Resolved by the
    /// Windows id as well, so the tests run on either platform.
    /// </summary>
    private static readonly TimeZoneInfo London = Resolve("Europe/London", "GMT Standard Time");

    private static readonly TimeOnly SevenAm = new(7, 0);

    private static readonly DayOfWeek[] Mondays = [DayOfWeek.Monday];

    [Fact]
    public void A_scan_later_today_is_due_today()
    {
        // Monday 2 November 2026, 05:00 local.
        var now = Instant(2026, 11, 2, 5, 0);

        var due = ScanSchedule.NextDue(now, SevenAm, Mondays, London);

        Assert.Equal(Instant(2026, 11, 2, 7, 0), due);
    }

    [Fact]
    public void A_scan_whose_time_has_passed_waits_for_the_next_scheduled_day()
    {
        // Monday 09:00; 07:00 has gone, and the only scheduled day is Monday.
        var now = Instant(2026, 11, 2, 9, 0);

        var due = ScanSchedule.NextDue(now, SevenAm, Mondays, London);

        // A week later, not later the same day and not tomorrow.
        Assert.Equal(Instant(2026, 11, 9, 7, 0), due);
    }

    [Fact]
    public void The_due_time_must_be_strictly_after_now()
    {
        // Exactly on the scheduled instant. Returning it unchanged would make the service run
        // the same scan twice, because it would wake, run, and compute the same due time again.
        var now = Instant(2026, 11, 2, 7, 0);

        var due = ScanSchedule.NextDue(now, SevenAm, Mondays, London);

        Assert.Equal(Instant(2026, 11, 9, 7, 0), due);
    }

    [Fact]
    public void The_nearest_of_several_scheduled_days_is_chosen()
    {
        // Tuesday 3 November, 12:00. Scheduled on Monday and Thursday.
        var now = Instant(2026, 11, 3, 12, 0);

        var due = ScanSchedule.NextDue(
            now, SevenAm, [DayOfWeek.Monday, DayOfWeek.Thursday], London);

        Assert.Equal(Instant(2026, 11, 5, 7, 0), due);
    }

    [Fact]
    public void A_daily_schedule_is_due_tomorrow_once_today_has_passed()
    {
        var now = Instant(2026, 11, 2, 9, 0);

        var due = ScanSchedule.NextDue(now, SevenAm, Enum.GetValues<DayOfWeek>(), London);

        Assert.Equal(Instant(2026, 11, 3, 7, 0), due);
    }

    [Fact]
    public void An_empty_day_list_can_never_fire()
    {
        // Null rather than a date far in the future, so the caller treats the schedule as off
        // instead of sleeping on a time that will never come.
        Assert.Null(ScanSchedule.NextDue(Instant(2026, 11, 2, 5, 0), SevenAm, [], London));
    }

    [Fact]
    public void The_local_time_is_held_across_a_clock_change()
    {
        // British Summer Time ends on Sunday 25 October 2026. A scan scheduled for 07:00 on
        // Mondays should run at 07:00 local both sides of it, which is a different UTC instant.
        // Each clock is set to 05:00 on the Monday itself, so the due time is that same morning
        // — from 08:00 on the 19th the next Monday would already be the 26th, past the change.
        var beforeTheChange = ScanSchedule.NextDue(
            Instant(2026, 10, 19, 5, 0), SevenAm, Mondays, London);

        var afterTheChange = ScanSchedule.NextDue(
            Instant(2026, 10, 26, 5, 0), SevenAm, Mondays, London);

        Assert.Equal(new TimeSpan(1, 0, 0), beforeTheChange!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, afterTheChange!.Value.Offset);

        // Both are 07:00 as the person reading the scan experiences it.
        Assert.Equal(7, TimeZoneInfo.ConvertTime(beforeTheChange.Value, London).Hour);
        Assert.Equal(7, TimeZoneInfo.ConvertTime(afterTheChange.Value, London).Hour);
    }

    [Fact]
    public void A_scheduled_time_that_does_not_exist_moves_forward_rather_than_being_skipped()
    {
        // The clocks go forward on Sunday 29 March 2026: 01:00 becomes 02:00, so 01:30 does not
        // happen. Skipping would mean a weekly scan silently missing a week.
        var oneThirty = new TimeOnly(1, 30);

        var due = ScanSchedule.NextDue(
            Instant(2026, 3, 28, 12, 0), oneThirty, [DayOfWeek.Sunday], London);

        Assert.NotNull(due);

        var local = TimeZoneInfo.ConvertTime(due.Value, London);

        // Landed on the far side of the gap, on the right day.
        Assert.Equal(new DateTime(2026, 3, 29), local.Date);
        Assert.False(London.IsInvalidTime(local.DateTime));
        Assert.True(local.Hour >= 2);
    }

    [Fact]
    public void An_ambiguous_scheduled_time_runs_once_at_the_earlier_instant()
    {
        // The clocks go back on Sunday 25 October 2026: 01:30 happens twice. The scan should
        // happen once, and sooner rather than later.
        var oneThirty = new TimeOnly(1, 30);

        var due = ScanSchedule.NextDue(
            Instant(2026, 10, 24, 12, 0), oneThirty, [DayOfWeek.Sunday], London);

        Assert.NotNull(due);

        // The pre-transition offset is the larger one, which is the earlier instant.
        Assert.Equal(new TimeSpan(1, 0, 0), due.Value.Offset);

        var local = TimeZoneInfo.ConvertTime(due.Value, London);
        Assert.Equal(new DateTime(2026, 10, 25), local.Date);
        Assert.Equal(1, local.Hour);
        Assert.Equal(30, local.Minute);
    }

    [Fact]
    public void Advancing_past_each_due_time_never_repeats_or_skips_a_week()
    {
        // Walks a year of Monday scans, which crosses both clock changes. A bug in the
        // strictly-after comparison shows up here as a duplicate or a gap.
        var cursor = Instant(2026, 1, 1, 0, 0);
        var seen = new List<DateTimeOffset>();

        for (var i = 0; i < 52; i++)
        {
            var due = ScanSchedule.NextDue(cursor, SevenAm, Mondays, London);
            Assert.NotNull(due);

            seen.Add(due.Value);
            cursor = due.Value;
        }

        Assert.Equal(52, seen.Distinct().Count());

        // Every one is a Monday at 07:00 local, and each is exactly a week after the last.
        foreach (var instant in seen)
        {
            var local = TimeZoneInfo.ConvertTime(instant, London);
            Assert.Equal(DayOfWeek.Monday, local.DayOfWeek);
            Assert.Equal(7, local.Hour);
        }

        for (var i = 1; i < seen.Count; i++)
        {
            var gap = TimeZoneInfo.ConvertTime(seen[i], London).Date
                      - TimeZoneInfo.ConvertTime(seen[i - 1], London).Date;

            Assert.Equal(TimeSpan.FromDays(7), gap);
        }
    }

    // --- delays ------------------------------------------------------------------------------

    [Fact]
    public void A_due_time_in_the_past_produces_no_delay_rather_than_a_negative_one()
    {
        var delay = ScanSchedule.DelayUntil(
            Instant(2026, 11, 2, 7, 0), Instant(2026, 11, 2, 9, 0));

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void The_delay_is_the_gap_to_the_due_time()
    {
        var delay = ScanSchedule.DelayUntil(
            Instant(2026, 11, 2, 7, 0), Instant(2026, 11, 2, 5, 30));

        Assert.Equal(TimeSpan.FromMinutes(90), delay);
    }

    [Theory]
    [InlineData(1, 60)]      // the first retry waits the initial delay
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    [InlineData(5, 900)]     // capped
    [InlineData(50, 900)]    // still capped, and no overflow
    public void Retries_back_off_exponentially_up_to_a_cap(int attempt, int expectedSeconds)
    {
        var delay = ScanSchedule.RetryDelay(
            attempt, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void A_very_large_attempt_number_cannot_overflow_the_delay()
    {
        // Doubling in ticks would overflow long before this without the guard, and a negative
        // delay would make the service hammer the API rather than back off.
        var delay = ScanSchedule.RetryDelay(
            int.MaxValue, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(15), delay);
    }

    private static DateTimeOffset Instant(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, London.GetUtcOffset(local));
    }

    private static TimeZoneInfo Resolve(string iana, string windows)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(iana);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windows);
        }
    }
}
