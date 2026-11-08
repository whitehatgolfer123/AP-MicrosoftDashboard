using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Data.Entities;

namespace MsAds.Auditor.Data.Stores;

/// <summary>
/// The durable record of portfolio scans, holding verdicts only.
/// </summary>
/// <remarks>
/// This is what makes a scheduled scan able to say "three accounts got worse since Tuesday"
/// rather than only what is true right now. See <see cref="ScanVerdict"/> for why the figures
/// behind each verdict are deliberately not stored.
/// </remarks>
public sealed class EfScanHistory(
    IDbContextFactory<AuditorDbContext> contextFactory,
    IOptions<PersistenceOptions> options,
    ILogger<EfScanHistory> logger) : IScanHistory
{
    private readonly int _limit = Math.Max(1, options.Value.ScanHistoryLimit);

    public async Task RecordAsync(ScanHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        db.ScanRuns.Add(new ScanRunEntity
        {
            RunId = entry.RunId,
            GeneratedAt = entry.GeneratedAt,
            Trigger = entry.Trigger,
            PeriodStart = entry.PeriodStart,
            PeriodEnd = entry.PeriodEnd,
            DurationMs = (long)entry.Duration.TotalMilliseconds,
            IsDemo = entry.IsDemo,
            FailureMessage = entry.FailureMessage,
            Accounts =
            [
                .. entry.Accounts.Select(a => new ScanVerdictEntity
                {
                    RunId = entry.RunId,
                    AccountId = a.AccountId,
                    CustomerId = a.CustomerId,
                    AccountName = a.AccountName,
                    Verdict = a.Verdict,
                    BasedOnConversions = a.BasedOnConversions,
                    Failed = a.Failed,
                }),
            ],
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Recorded {Trigger} scan {RunId} of {Count} account(s)",
            entry.Trigger, entry.RunId, entry.Accounts.Count);

        await TrimAsync(db, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScanHistoryEntry>> RecentAsync(
        int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var runs = await db.ScanRuns
            .AsNoTracking()
            .Include(r => r.Accounts)
            .OrderByDescending(r => r.GeneratedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. runs.Select(ToDomain)];
    }

    public async Task<ScanHistoryEntry?> GetAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var run = await db.ScanRuns
            .AsNoTracking()
            .Include(r => r.Accounts)
            .FirstOrDefaultAsync(r => r.RunId == runId, cancellationToken)
            .ConfigureAwait(false);

        return run is null ? null : ToDomain(run);
    }

    public async Task<IReadOnlyDictionary<long, HealthVerdict>> VerdictsBeforeAsync(
        DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Each account's most recent verdict from any run before the cutoff — not simply the
        // verdicts of the single previous run. An account missing from that one run (it failed,
        // or was added to the portfolio later) would otherwise look like a brand new account
        // and its deterioration would go unreported.
        var candidates = await db.ScanVerdicts
            .AsNoTracking()
            .Where(v => v.Run!.GeneratedAt < before && v.Run!.FailureMessage == null && !v.Failed)
            .OrderByDescending(v => v.Run!.GeneratedAt)
            .Select(v => new { v.AccountId, v.Verdict })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var verdicts = new Dictionary<long, HealthVerdict>();

        foreach (var candidate in candidates)
        {
            // Ordered newest-first, so the first sighting of an account is its latest verdict.
            verdicts.TryAdd(candidate.AccountId, candidate.Verdict);
        }

        return verdicts;
    }

    private static ScanHistoryEntry ToDomain(ScanRunEntity entity) => new()
    {
        RunId = entity.RunId,
        GeneratedAt = entity.GeneratedAt,
        Trigger = entity.Trigger,
        PeriodStart = entity.PeriodStart,
        PeriodEnd = entity.PeriodEnd,
        Duration = TimeSpan.FromMilliseconds(entity.DurationMs),
        IsDemo = entity.IsDemo,
        FailureMessage = entity.FailureMessage,
        Accounts =
        [
            .. entity.Accounts
                .OrderBy(a => a.AccountName, StringComparer.OrdinalIgnoreCase)
                .Select(a => new ScanVerdict
                {
                    AccountId = a.AccountId,
                    CustomerId = a.CustomerId,
                    AccountName = a.AccountName,
                    Verdict = a.Verdict,
                    BasedOnConversions = a.BasedOnConversions,
                    Failed = a.Failed,
                }),
        ],
    };

    private async Task TrimAsync(AuditorDbContext db, CancellationToken cancellationToken)
    {
        var total = await db.ScanRuns.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total <= _limit)
        {
            return;
        }

        var stale = await db.ScanRuns
            .OrderByDescending(r => r.GeneratedAt)
            .Skip(_limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        db.ScanRuns.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Trimmed {Count} scan run(s) beyond the configured limit of {Limit}", stale.Count, _limit);
    }
}
