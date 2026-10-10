using System.ComponentModel.DataAnnotations;
using MsAds.Auditor.BingAds;
using MsAds.Auditor.Core.Settings;

namespace MsAds.Auditor.Web.Services;

public enum AuditorMode
{
    /// <summary>Synthetic data only. No credentials required, nothing contacts Microsoft.</summary>
    Demo,

    /// <summary>Live data from the Microsoft Advertising API.</summary>
    Live,
}

/// <summary>
/// Application configuration. Credentials are read from configuration providers
/// (user secrets, environment variables) and never from source control.
/// </summary>
public sealed class AuditorOptions
{
    public const string SectionName = "Auditor";

    /// <summary>
    /// Whether to serve demo data or live data. Demo is the default so a fresh checkout runs
    /// without credentials.
    /// </summary>
    public AuditorMode Mode { get; set; } = AuditorMode.Demo;

    /// <summary>How many generated reports to keep in memory for the report history.</summary>
    [Range(1, 200)]
    public int ReportHistoryLimit { get; set; } = 25;

    /// <summary>Default analysis thresholds, overridable per report in the Settings screen.</summary>
    public AuditSettings Defaults { get; set; } = new();

    public MicrosoftAdvertisingOptions MicrosoftAdvertising { get; set; } = new();
}
