using System.ComponentModel.DataAnnotations;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// When, and whether, a portfolio scan runs unattended.
/// </summary>
/// <remarks>
/// Off by default. A scheduled scan makes API calls against every reachable client account
/// without anyone asking it to, so switching it on is a deliberate decision — and it is pointless
/// before live reporting is trusted, since a schedule that scans demo data tells nobody anything.
/// </remarks>
public sealed class ScheduleOptions
{
    public const string SectionName = "Auditor:Schedule";

    public bool Enabled { get; init; }

    /// <summary>
    /// Local time of day the scan starts, as <c>HH:mm</c>. Early enough that the result is
    /// waiting when someone arrives.
    /// </summary>
    public string TimeOfDay { get; init; } = "07:00";

    /// <summary>
    /// Days the scan runs, as configured. Empty means "use <see cref="DefaultDays"/>".
    /// </summary>
    /// <remarks>
    /// Deliberately defaulted to empty rather than to Monday, and the fallback applied in
    /// <see cref="ResolvedDays"/> instead. The configuration binder ADDS to a collection
    /// property that already holds items rather than replacing it, so a default of
    /// <c>[Monday]</c> plus a configured <c>["Friday"]</c> binds to <c>[Monday, Friday]</c> —
    /// giving an unasked-for Monday sweep of every client account. Verified behaviour, not a
    /// precaution.
    /// </remarks>
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];

    /// <summary>
    /// Monday only. A weekly direction-of-travel reading is what the roadmap asks for, and a
    /// daily full-portfolio sweep is a lot of API calls for a signal that moves slowly.
    /// </summary>
    public static IReadOnlyList<DayOfWeek> DefaultDays => [DayOfWeek.Monday];

    /// <summary>
    /// The days to run on: the configured list, de-duplicated and ordered, or the default when
    /// nothing is configured.
    /// </summary>
    public IReadOnlyList<DayOfWeek> ResolvedDays => Days.Count == 0
        ? DefaultDays
        : [.. Days.Distinct().OrderBy(day => day)];

    /// <summary>
    /// The time zone the schedule is expressed in, so the local time holds across a clock
    /// change. An IANA or Windows id; falls back to the machine's own zone when unset.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>The window each scheduled scan covers.</summary>
    public ReportingPeriod Period { get; init; } = ReportingPeriod.Last7Days;

    /// <summary>
    /// How many accounts are queried at once. Kept low deliberately: Microsoft Advertising
    /// throttles concurrent report requests, and a full manager hierarchy must never be fanned
    /// into simultaneous calls.
    /// </summary>
    [Range(1, 16)]
    public int MaxConcurrency { get; init; } = 4;

    /// <summary>How many times a failed scan is retried before it is left until the next run.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; init; } = 3;

    /// <summary>The first retry delay; each subsequent one doubles, up to the maximum.</summary>
    [Range(1, 3600)]
    public int InitialRetrySeconds { get; init; } = 60;

    [Range(1, 86400)]
    public int MaxRetrySeconds { get; init; } = 900;

    public TimeSpan InitialRetryDelay => TimeSpan.FromSeconds(InitialRetrySeconds);

    public TimeSpan MaximumRetryDelay => TimeSpan.FromSeconds(MaxRetrySeconds);

    /// <summary>
    /// The scheduled time of day, or 07:00 if the configured value cannot be parsed. A
    /// mistyped time should not stop the application starting; the resolved value is logged.
    /// </summary>
    public TimeOnly ResolvedTimeOfDay =>
        TimeOnly.TryParse(TimeOfDay, out var parsed) ? parsed : new TimeOnly(7, 0);

    public bool TimeOfDayIsValid => TimeOnly.TryParse(TimeOfDay, out _);

    /// <summary>
    /// The schedule's time zone, falling back to the machine's local zone when unset or unknown.
    /// </summary>
    public TimeZoneInfo ResolvedTimeZone
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TimeZone))
            {
                return TimeZoneInfo.Local;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            }
            catch (Exception exception) when (
                exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.Local;
            }
        }
    }

    public bool TimeZoneIsValid =>
        string.IsNullOrWhiteSpace(TimeZone) || ResolvedTimeZone.Id != TimeZoneInfo.Local.Id
        || string.Equals(TimeZone, TimeZoneInfo.Local.Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>Configured values that cannot be used, for the startup check to report.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (!TimeOfDayIsValid)
        {
            problems.Add(
                $"{SectionName}:TimeOfDay is \"{TimeOfDay}\", which is not a time. Using 07:00.");
        }

        if (!TimeZoneIsValid)
        {
            problems.Add(
                $"{SectionName}:TimeZone is \"{TimeZone}\", which this machine does not recognise. "
                + $"Using {TimeZoneInfo.Local.Id}.");
        }

        return problems;
    }
}
