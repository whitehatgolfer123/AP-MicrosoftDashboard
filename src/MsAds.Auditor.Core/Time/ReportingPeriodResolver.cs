namespace MsAds.Auditor.Core.Time;

/// <summary>
/// Turns a <see cref="ReportingPeriod"/> selection into explicit dates, in the account's own
/// reporting time zone, excluding the incomplete current day by default.
/// </summary>
public sealed class ReportingPeriodResolver(IClock clock)
{
    private readonly IClock _clock = clock;

    /// <param name="period">The selected window.</param>
    /// <param name="accountTimeZone">
    /// The account's reporting time zone. When <see langword="null"/>, UTC is assumed and the
    /// result records that fallback so the user can be told.
    /// </param>
    /// <param name="excludeCurrentDay">
    /// Leave today out of the range. Default <see langword="true"/>: today's data is still
    /// accumulating, so including it makes period-on-period comparisons misleading.
    /// </param>
    /// <param name="includeComparison">Also resolve the preceding equivalent period.</param>
    public ResolvedPeriod Resolve(
        ReportingPeriod period,
        TimeZoneInfo? accountTimeZone,
        bool excludeCurrentDay = true,
        bool includeComparison = true)
    {
        var fellBack = accountTimeZone is null;
        var timeZone = accountTimeZone ?? TimeZoneInfo.Utc;

        var accountNow = TimeZoneInfo.ConvertTime(_clock.UtcNow, timeZone);
        var accountToday = DateOnly.FromDateTime(accountNow.Date);

        var end = excludeCurrentDay ? accountToday.AddDays(-1) : accountToday;
        var current = new DateRange(end.AddDays(-(period.Days() - 1)), end);

        return new ResolvedPeriod
        {
            Period = period,
            Current = current,
            Previous = includeComparison ? current.PrecedingPeriod() : null,
            AccountToday = accountToday,
            TimeZoneId = timeZone.Id,
            TimeZoneDisplayName = timeZone.DisplayName,
            CurrentDayExcluded = excludeCurrentDay,
            TimeZoneFellBackToUtc = fellBack,
        };
    }
}
