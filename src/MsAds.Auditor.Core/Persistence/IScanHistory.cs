using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.Core.Persistence;

/// <summary>What caused a scan to run.</summary>
public enum ScanTrigger
{
    /// <summary>A person pressed Scan.</summary>
    Manual,

    /// <summary>A background schedule started it.</summary>
    Scheduled,
}

/// <summary>
/// One account's verdict in a recorded scan.
/// </summary>
/// <remarks>
/// Carries the verdict and nothing that would reveal the account's figures. In particular
/// <see cref="AccountHealth.Reason"/> is deliberately not stored: it is prose containing the
/// account's own spend and conversion numbers ("spend fell from £1,200 to £300"), and storing it
/// would put client performance data on disk through the back door. A verdict transition is
/// enough to say an account needs attention, and the figures behind it are re-read live when
/// someone opens the account.
/// </remarks>
public sealed record ScanVerdict
{
    public required long AccountId { get; init; }

    /// <summary>The manager (customer) id that owns the account.</summary>
    public required long CustomerId { get; init; }

    public required string AccountName { get; init; }

    public required HealthVerdict Verdict { get; init; }

    /// <summary>
    /// False when the verdict rested on cost and traffic alone, because the account returned no
    /// conversion data. Stored so a recorded "Improving" is not over-read later.
    /// </summary>
    public required bool BasedOnConversions { get; init; }

    /// <summary>True when this account could not be scanned at all.</summary>
    public bool Failed { get; init; }
}

/// <summary>
/// A recorded scan: when it ran, over what dates, and the verdict reached for each account.
/// </summary>
public sealed record ScanHistoryEntry
{
    public required Guid RunId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required ScanTrigger Trigger { get; init; }

    /// <summary>The scanned window. Stored as dates so a run can be reproduced exactly.</summary>
    public required DateOnly PeriodStart { get; init; }

    public required DateOnly PeriodEnd { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>True when the figures behind these verdicts were synthetic.</summary>
    public required bool IsDemo { get; init; }

    public IReadOnlyList<ScanVerdict> Accounts { get; init; } = [];

    /// <summary>
    /// Set when the scan itself failed rather than an individual account, so a scheduled run
    /// that never produced verdicts is still visible as having been attempted.
    /// </summary>
    public string? FailureMessage { get; init; }

    public bool Failed => FailureMessage is not null;

    public int CountOf(HealthVerdict verdict) => Accounts.Count(a => a.Verdict == verdict);

    /// <summary>
    /// Reduces a completed scan to the verdicts worth keeping, dropping every metric the scan
    /// held in memory.
    /// </summary>
    public static ScanHistoryEntry From(
        PortfolioScan scan, ScanTrigger trigger, Guid runId) => new()
        {
            RunId = runId,
            GeneratedAt = scan.GeneratedAt,
            Trigger = trigger,
            PeriodStart = scan.Period.Current.Start,
            PeriodEnd = scan.Period.Current.End,
            Duration = scan.Duration,
            IsDemo = scan.IsDemo,
            Accounts =
            [
                .. scan.Accounts.Select(a => new ScanVerdict
                {
                    AccountId = a.Account.AccountId,
                    CustomerId = a.Account.CustomerId,
                    AccountName = a.Account.AccountName,
                    Verdict = a.Verdict,
                    BasedOnConversions = a.BasedOnConversions,
                    Failed = a.Failed,
                }),
            ],
        };

    /// <summary>Records a scan that failed before it produced any verdicts.</summary>
    public static ScanHistoryEntry ForFailure(
        Guid runId,
        DateTimeOffset at,
        ScanTrigger trigger,
        DateRange period,
        bool isDemo,
        string message) => new()
        {
            RunId = runId,
            GeneratedAt = at,
            Trigger = trigger,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            IsDemo = isDemo,
            FailureMessage = message,
        };
}

/// <summary>
/// How one account's verdict moved between two scans.
/// </summary>
public sealed record VerdictChange
{
    public required long AccountId { get; init; }

    public required string AccountName { get; init; }

    public required HealthVerdict From { get; init; }

    public required HealthVerdict To { get; init; }

    /// <summary>
    /// True when the account moved to a verdict that needs attention sooner than the one it
    /// held before. This is what a scheduled scan notifies on.
    /// </summary>
    public bool IsDeterioration => AttentionRank(To) < AttentionRank(From);

    /// <summary>
    /// Lower sorts first, matching <see cref="AccountHealth.SortWeight"/>. Dormant is ranked
    /// beside Stable rather than last here: moving from Stable to Dormant means an account
    /// stopped spending, which is worth knowing, whereas in a scan listing a dormant account is
    /// simply uninteresting.
    /// </summary>
    private static int AttentionRank(HealthVerdict verdict) => verdict switch
    {
        HealthVerdict.NotDelivering => 0,
        HealthVerdict.Declining => 1,
        HealthVerdict.Dormant => 2,
        HealthVerdict.Unknown => 3,
        HealthVerdict.Stable => 4,
        HealthVerdict.Improving => 5,
        _ => 6,
    };
}

/// <summary>
/// The durable record of portfolio scans, so a scheduled scan can say what changed since the
/// last one rather than only what is true now.
/// </summary>
/// <remarks>
/// Stores verdicts only, never the figures behind them. See <see cref="ScanVerdict"/>.
/// </remarks>
public interface IScanHistory
{
    Task RecordAsync(ScanHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Runs most recent first.</summary>
    Task<IReadOnlyList<ScanHistoryEntry>> RecentAsync(
        int limit = 50, CancellationToken cancellationToken = default);

    Task<ScanHistoryEntry?> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The verdict each account held in the most recent completed scan before
    /// <paramref name="before"/>. Used to work out what has deteriorated.
    /// </summary>
    Task<IReadOnlyDictionary<long, HealthVerdict>> VerdictsBeforeAsync(
        DateTimeOffset before, CancellationToken cancellationToken = default);
}

/// <summary>
/// Used when persistence is switched off. Records nothing, so a scheduled scan still runs but
/// cannot report on what changed since last time.
/// </summary>
public sealed class NullScanHistory : IScanHistory
{
    public Task RecordAsync(ScanHistoryEntry entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ScanHistoryEntry>> RecentAsync(
        int limit = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ScanHistoryEntry>>([]);

    public Task<ScanHistoryEntry?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
        => Task.FromResult<ScanHistoryEntry?>(null);

    public Task<IReadOnlyDictionary<long, HealthVerdict>> VerdictsBeforeAsync(
        DateTimeOffset before, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<long, HealthVerdict>>(
            new Dictionary<long, HealthVerdict>());
}

/// <summary>Compares two sets of verdicts to find the accounts that have got worse.</summary>
public static class VerdictComparison
{
    /// <summary>
    /// Accounts whose verdict moved at all between <paramref name="previous"/> and
    /// <paramref name="current"/>, deteriorations first. An account absent from
    /// <paramref name="previous"/> is new to the scan and is not reported as a change, because
    /// there is nothing to compare it against.
    /// </summary>
    public static IReadOnlyList<VerdictChange> Between(
        IReadOnlyDictionary<long, HealthVerdict> previous,
        IReadOnlyList<ScanVerdict> current)
    {
        var changes = new List<VerdictChange>();

        foreach (var account in current)
        {
            if (!previous.TryGetValue(account.AccountId, out var before) || before == account.Verdict)
            {
                continue;
            }

            changes.Add(new VerdictChange
            {
                AccountId = account.AccountId,
                AccountName = account.AccountName,
                From = before,
                To = account.Verdict,
            });
        }

        return
        [
            .. changes
                .OrderByDescending(c => c.IsDeterioration)
                .ThenBy(c => c.AccountName, StringComparer.OrdinalIgnoreCase),
        ];
    }
}
