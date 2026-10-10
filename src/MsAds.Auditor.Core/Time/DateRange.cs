namespace MsAds.Auditor.Core.Time;

/// <summary>
/// An inclusive range of reporting dates. Both <see cref="Start"/> and <see cref="End"/> are
/// counted, so a "last 7 days" range spans 7 distinct dates.
/// </summary>
public readonly record struct DateRange
{
    public DateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end), $"End date {end:yyyy-MM-dd} precedes start date {start:yyyy-MM-dd}.");
        }

        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    /// <summary>Number of distinct dates in the range, counting both endpoints.</summary>
    public int Days => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public IEnumerable<DateOnly> EachDay()
    {
        for (var day = Start; day <= End; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    /// <summary>
    /// The equivalent range immediately preceding this one, with the same number of days.
    /// </summary>
    public DateRange PrecedingPeriod()
    {
        var previousEnd = Start.AddDays(-1);
        return new DateRange(previousEnd.AddDays(-(Days - 1)), previousEnd);
    }

    public override string ToString() => $"{Start:yyyy-MM-dd} to {End:yyyy-MM-dd}";
}
