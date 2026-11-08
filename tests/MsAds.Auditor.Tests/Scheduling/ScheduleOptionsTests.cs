using Microsoft.Extensions.Configuration;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Tests.Scheduling;

/// <summary>
/// How the schedule configuration binds.
/// </summary>
public class ScheduleOptionsTests
{
    [Fact]
    public void Configuring_the_days_replaces_the_default_rather_than_adding_to_it()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auditor:Schedule:Days:0"] = "Friday",
        });

        // The configuration binder ADDS to a collection property that already holds items. With
        // a property default of [Monday] this bound to [Monday, Friday], which would have swept
        // every client account on a day nobody asked for. The default is applied on read
        // instead, so configuring Friday means Friday.
        Assert.Equal([DayOfWeek.Friday], options.ResolvedDays);
    }

    [Fact]
    public void Configuring_no_days_falls_back_to_the_default()
    {
        var options = Bind(new Dictionary<string, string?>());

        Assert.Equal([DayOfWeek.Monday], options.ResolvedDays);
    }

    [Fact]
    public void Duplicate_days_are_collapsed_and_ordered()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auditor:Schedule:Days:0"] = "Friday",
            ["Auditor:Schedule:Days:1"] = "Monday",
            ["Auditor:Schedule:Days:2"] = "Friday",
        });

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], options.ResolvedDays);
    }

    [Fact]
    public void An_unparseable_time_of_day_falls_back_and_is_reported()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auditor:Schedule:TimeOfDay"] = "half past seven",
        });

        // A mistyped time should not stop the application starting, but it must not pass
        // silently either.
        Assert.Equal(new TimeOnly(7, 0), options.ResolvedTimeOfDay);
        Assert.False(options.TimeOfDayIsValid);
        Assert.Contains(options.Problems(), p => p.Contains("TimeOfDay"));
    }

    [Fact]
    public void A_valid_time_of_day_is_used_and_raises_no_problem()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auditor:Schedule:TimeOfDay"] = "06:45",
        });

        Assert.Equal(new TimeOnly(6, 45), options.ResolvedTimeOfDay);
        Assert.True(options.TimeOfDayIsValid);
        Assert.Empty(options.Problems());
    }

    [Fact]
    public void An_unknown_time_zone_falls_back_to_the_machine_zone_and_is_reported()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Auditor:Schedule:TimeZone"] = "Mars/Olympus_Mons",
        });

        Assert.Equal(TimeZoneInfo.Local.Id, options.ResolvedTimeZone.Id);
        Assert.Contains(options.Problems(), p => p.Contains("TimeZone"));
    }

    [Fact]
    public void Scheduled_scans_are_off_unless_switched_on()
    {
        // A scheduled scan calls the API against every reachable client account without anyone
        // asking, so it must never be on by default.
        Assert.False(Bind(new Dictionary<string, string?>()).Enabled);
    }

    private static ScheduleOptions Bind(IDictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build()
            .GetSection(ScheduleOptions.SectionName)
            .Get<ScheduleOptions>() ?? new ScheduleOptions();
}
