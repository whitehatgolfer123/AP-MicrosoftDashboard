using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Persistence;

namespace MsAds.Auditor.Data.Entities;

/// <summary>One recorded portfolio scan.</summary>
internal sealed class ScanRunEntity
{
    public Guid RunId { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public ScanTrigger Trigger { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    /// <summary>
    /// Stored in milliseconds rather than as a TimeSpan, because SQLite has no interval type and
    /// a provider-neutral integer reads the same on both.
    /// </summary>
    public long DurationMs { get; set; }

    public bool IsDemo { get; set; }

    /// <summary>Set when the scan failed before producing any verdicts.</summary>
    public string? FailureMessage { get; set; }

    public List<ScanVerdictEntity> Accounts { get; set; } = [];
}

/// <summary>
/// One account's verdict within a recorded scan.
/// </summary>
/// <remarks>
/// Verdict only. No spend, clicks, conversions or the prose reason that would quote them — see
/// <see cref="ScanVerdict"/> for why. This table is the one place account identity is written to
/// disk alongside a judgement, and it stays limited to what a "this account needs attention"
/// message requires.
/// </remarks>
internal sealed class ScanVerdictEntity
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public ScanRunEntity? Run { get; set; }

    public long AccountId { get; set; }

    public long CustomerId { get; set; }

    public string AccountName { get; set; } = string.Empty;

    public HealthVerdict Verdict { get; set; }

    public bool BasedOnConversions { get; set; }

    public bool Failed { get; set; }
}
