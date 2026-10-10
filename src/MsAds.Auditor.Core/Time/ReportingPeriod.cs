namespace MsAds.Auditor.Core.Time;

/// <summary>Reporting windows the user can select.</summary>
public enum ReportingPeriod
{
    Last7Days = 7,
    Last14Days = 14,
    Last30Days = 30,
}

public static class ReportingPeriodExtensions
{
    /// <summary>Number of days the period covers.</summary>
    public static int Days(this ReportingPeriod period) => (int)period;

    public static string DisplayName(this ReportingPeriod period) => period switch
    {
        ReportingPeriod.Last7Days => "Last 7 days",
        ReportingPeriod.Last14Days => "Last 14 days",
        ReportingPeriod.Last30Days => "Last 30 days",
        _ => $"Last {(int)period} days",
    };
}
