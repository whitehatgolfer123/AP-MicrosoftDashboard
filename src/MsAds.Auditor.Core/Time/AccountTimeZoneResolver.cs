namespace MsAds.Auditor.Core.Time;

/// <summary>
/// Turns the time zone an account reports into a <see cref="TimeZoneInfo"/>.
/// </summary>
/// <remarks>
/// Microsoft Advertising reports an account's time zone as a <c>TimeZoneType</c> value such as
/// <c>GMTStandardTime</c>, which is neither an IANA identifier nor a Windows identifier. The map
/// below covers the values seen most often; anything unrecognised resolves to
/// <see langword="null"/> so the caller falls back to UTC and tells the user it did, rather than
/// silently reporting on the wrong days.
/// </remarks>
public static class AccountTimeZoneResolver
{
    private static readonly Dictionary<string, string> MicrosoftAdvertisingToIana =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["GMTStandardTime"] = "Europe/London",
            ["GreenwichMeanTimeDublinEdinburghLisbonLondon"] = "Europe/London",
            ["WEuropeStandardTime"] = "Europe/Berlin",
            ["RomanceStandardTime"] = "Europe/Paris",
            ["CentralEuropeStandardTime"] = "Europe/Budapest",
            ["CentralEuropeanStandardTime"] = "Europe/Warsaw",
            ["EEuropeStandardTime"] = "Europe/Bucharest",
            ["GTBStandardTime"] = "Europe/Athens",
            ["EasternTimeUSCanada"] = "America/New_York",
            ["CentralTimeUSCanada"] = "America/Chicago",
            ["MountainTimeUSCanada"] = "America/Denver",
            ["PacificTimeUSCanada"] = "America/Los_Angeles",
            ["PacificTimeUSCanadaTijuana"] = "America/Los_Angeles",
            ["AlaskanStandardTime"] = "America/Anchorage",
            ["HawaiianStandardTime"] = "Pacific/Honolulu",
            ["ArizonaStandardTime"] = "America/Phoenix",
            ["AtlanticTimeCanada"] = "America/Halifax",
            ["AUSEasternStandardTime"] = "Australia/Sydney",
            ["AUSCentralStandardTime"] = "Australia/Darwin",
            ["WAustraliaStandardTime"] = "Australia/Perth",
            ["NewZealandStandardTime"] = "Pacific/Auckland",
            ["TokyoStandardTime"] = "Asia/Tokyo",
            ["ChinaStandardTime"] = "Asia/Shanghai",
            ["SingaporeStandardTime"] = "Asia/Singapore",
            ["IndiaStandardTime"] = "Asia/Kolkata",
            ["ArabStandardTime"] = "Asia/Riyadh",
            ["ArabianStandardTime"] = "Asia/Dubai",
            ["IsraelStandardTime"] = "Asia/Jerusalem",
            ["SouthAfricaStandardTime"] = "Africa/Johannesburg",
            ["EAfricaStandardTime"] = "Africa/Nairobi",
            ["BrasiliaStandardTime"] = "America/Sao_Paulo",
            ["ArgentinaStandardTime"] = "America/Argentina/Buenos_Aires",
            ["SAPacificStandardTime"] = "America/Bogota",
            ["MexicoCityTijuanaMonterrey"] = "America/Mexico_City",
            ["RussianStandardTime"] = "Europe/Moscow",
            ["TurkeyStandardTime"] = "Europe/Istanbul",
            ["UTC"] = "UTC",
        };

    /// <summary>
    /// Resolves a reported time zone, or <see langword="null"/> when it cannot be mapped.
    /// </summary>
    public static TimeZoneInfo? Resolve(string? reportedTimeZone)
    {
        if (string.IsNullOrWhiteSpace(reportedTimeZone))
        {
            return null;
        }

        var value = reportedTimeZone.Trim();

        // Many accounts report a value the platform already understands.
        if (TryFind(value, out var direct))
        {
            return direct;
        }

        // Otherwise translate the Microsoft Advertising name, ignoring any spaces it carries.
        var key = value.Replace(" ", string.Empty);

        return MicrosoftAdvertisingToIana.TryGetValue(key, out var iana) && TryFind(iana, out var mapped)
            ? mapped
            : null;
    }

    private static bool TryFind(string id, out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = null;
            return false;
        }
    }
}
