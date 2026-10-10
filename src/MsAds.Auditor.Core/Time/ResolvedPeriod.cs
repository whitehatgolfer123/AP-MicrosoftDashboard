namespace MsAds.Auditor.Core.Time;

/// <summary>
/// The concrete dates a report will use, together with the reasoning the user needs to see:
/// which time zone decided "today", and whether the current (partial) day was excluded.
/// </summary>
public sealed record ResolvedPeriod
{
    public required ReportingPeriod Period { get; init; }

    /// <summary>The selected reporting window.</summary>
    public required DateRange Current { get; init; }

    /// <summary>
    /// The immediately preceding window of equal length, or <see langword="null"/> when the user
    /// asked for no comparison.
    /// </summary>
    public DateRange? Previous { get; init; }

    /// <summary>"Today" in the account's reporting time zone, not the server's.</summary>
    public required DateOnly AccountToday { get; init; }

    /// <summary>The time zone used to determine <see cref="AccountToday"/>.</summary>
    public required string TimeZoneId { get; init; }

    /// <summary>Display label for the time zone, e.g. "(UTC+00:00) GMT Standard Time".</summary>
    public string? TimeZoneDisplayName { get; init; }

    /// <summary>
    /// True when the current, still-incomplete day was left out of <see cref="Current"/>.
    /// </summary>
    public required bool CurrentDayExcluded { get; init; }

    /// <summary>
    /// True when the account's reporting time zone could not be resolved and UTC was assumed.
    /// </summary>
    public bool TimeZoneFellBackToUtc { get; init; }

    public bool HasComparison => Previous is not null;
}
