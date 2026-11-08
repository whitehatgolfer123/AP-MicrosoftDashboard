namespace MsAds.Auditor.Core.Scheduling;

/// <summary>
/// When a scheduled scan is next due.
/// </summary>
/// <remarks>
/// <para>
/// Pure and separate from the background service that acts on it, because the awkward parts of a
/// schedule are all in the arithmetic: a time that has already passed today, a day-of-week list
/// that has to wrap into next week, and a local time that does not exist or happens twice on the
/// days the clocks change.
/// </para>
/// <para>
/// The schedule is expressed in a named time zone rather than UTC so that "07:00 on Monday"
/// stays 07:00 after the clocks change. An agency looking at a Monday-morning scan wants it
/// before they arrive, not an hour later for half the year.
/// </para>
/// </remarks>
public static class ScanSchedule
{
    /// <summary>
    /// The next instant at or after <paramref name="after"/> that matches
    /// <paramref name="timeOfDay"/> on one of <paramref name="days"/>, in
    /// <paramref name="timeZone"/>.
    /// </summary>
    /// <returns>
    /// Null when <paramref name="days"/> is empty, which means the schedule can never fire and
    /// the caller should treat the schedule as switched off rather than wait forever.
    /// </returns>
    public static DateTimeOffset? NextDue(
        DateTimeOffset after,
        TimeOnly timeOfDay,
        IReadOnlyCollection<DayOfWeek> days,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (days.Count == 0)
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(after, timeZone);

        // Eight days rather than seven: if the only scheduled day is today and the time has
        // already gone, the answer is the same weekday next week, which is day seven.
        for (var offset = 0; offset <= 7; offset++)
        {
            var date = DateOnly.FromDateTime(localNow.Date).AddDays(offset);

            if (!days.Contains(date.DayOfWeek))
            {
                continue;
            }

            var candidate = Resolve(date, timeOfDay, timeZone);

            if (candidate is { } due && due > after)
            {
                return due;
            }
        }

        return null;
    }

    /// <summary>
    /// Turns a local date and time into an instant, coping with the two days a year when a
    /// local time is not a single instant.
    /// </summary>
    /// <remarks>
    /// On the spring-forward day the scheduled time may not exist at all — 01:30 where the
    /// clocks jump 01:00 to 02:00. Skipping the run would mean a weekly scan silently missing a
    /// week, so the schedule moves forward to the first valid instant instead.
    ///
    /// On the autumn day it may exist twice. The earlier of the two is used, so the scan happens
    /// once and happens sooner rather than being run twice.
    /// </remarks>
    private static DateTimeOffset? Resolve(DateOnly date, TimeOnly timeOfDay, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(timeOfDay, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            // Walk forward a minute at a time to the far side of the gap. A gap is an hour in
            // every real time zone, so this terminates quickly.
            for (var minutes = 1; minutes <= 180; minutes++)
            {
                var shifted = local.AddMinutes(minutes);

                if (!timeZone.IsInvalidTime(shifted))
                {
                    return new DateTimeOffset(shifted, timeZone.GetUtcOffset(shifted));
                }
            }

            return null;
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            // The ambiguous offsets come back ordered; the largest is the pre-transition one,
            // which is the earlier instant.
            var offsets = timeZone.GetAmbiguousTimeOffsets(local);
            return new DateTimeOffset(local, offsets.Max());
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    /// <summary>
    /// How long to wait before the given due time, floored at zero so an already-passed due time
    /// never produces a negative delay.
    /// </summary>
    public static TimeSpan DelayUntil(DateTimeOffset due, DateTimeOffset now)
    {
        var delay = due - now;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    /// <summary>
    /// The wait before retry attempt <paramref name="attempt"/>, doubling each time and capped.
    /// </summary>
    /// <remarks>
    /// Attempt 1 is the first retry. Microsoft Advertising throttles report requests, and a
    /// failed portfolio scan is very often throttling rather than a real fault, so retrying
    /// immediately would make it worse.
    /// </remarks>
    public static TimeSpan RetryDelay(int attempt, TimeSpan initial, TimeSpan maximum)
    {
        if (attempt <= 1)
        {
            return initial;
        }

        var ticks = initial.Ticks;

        for (var i = 1; i < attempt; i++)
        {
            // Doubling in ticks, watching for the overflow a large attempt number would cause.
            if (ticks > maximum.Ticks / 2)
            {
                return maximum;
            }

            ticks *= 2;
        }

        return ticks > maximum.Ticks ? maximum : TimeSpan.FromTicks(ticks);
    }
}
