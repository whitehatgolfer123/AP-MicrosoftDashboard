using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Data.Mapping;

namespace MsAds.Auditor.Data.Stores;

/// <summary>
/// The database-backed change log: the permanent record of what this tool altered in a client's
/// account, by whom, and with what result.
/// </summary>
/// <remarks>
/// <para>
/// Append-only. There is no update or delete path, and the only trimming is an optional cap that
/// defaults to off, because an audit trail that forgets is not one.
/// </para>
/// <para>
/// Each call owns its own context, taken from the factory rather than injected, because a change
/// run is recorded from a Razor page handler whose scope may already have been disposed by the
/// time a background continuation writes.
/// </para>
/// </remarks>
public sealed class EfChangeLog(
    IDbContextFactory<AuditorDbContext> contextFactory,
    IOptions<PersistenceOptions> options,
    ILogger<EfChangeLog> logger) : IChangeLog
{
    private readonly int _limit = options.Value.ChangeLogLimit;

    public async Task RecordAsync(ChangeRunResult result, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        db.ChangeRuns.Add(ChangeLogMapping.ToEntity(result));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Recorded change run {RunId} on account {AccountId}: {Applied} applied, {Failed} failed, preview {Preview}",
            result.RunId, result.AccountId, result.AppliedCount, result.FailedCount, result.WasPreview);

        if (_limit > 0)
        {
            await TrimAsync(db, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ChangeRunResult>> RecentAsync(
        int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var runs = await db.ChangeRuns
            .AsNoTracking()
            .Include(r => r.Outcomes)
            .OrderByDescending(r => r.RanAt)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. runs.Select(ChangeLogMapping.ToDomain)];
    }

    public async Task<IReadOnlySet<string>> AppliedChangeIdsAsync(
        long accountId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Filtered on the outcome's own account id and status, which is what the index covers.
        // Preview runs are excluded through the run: a previewed change was never sent, so it
        // must stay on the proposal list.
        var ids = await db.ChangeOutcomes
            .AsNoTracking()
            .Where(o => o.ChangeAccountId == accountId
                        && o.Status == ChangeStatus.Applied
                        && !o.Run!.WasPreview)
            .Select(o => o.ChangeId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Drops the oldest runs beyond the configured cap. Only used when a limit is set; the
    /// cascade removes their outcomes.
    /// </summary>
    private async Task TrimAsync(AuditorDbContext db, CancellationToken cancellationToken)
    {
        var total = await db.ChangeRuns.CountAsync(cancellationToken).ConfigureAwait(false);

        if (total <= _limit)
        {
            return;
        }

        var stale = await db.ChangeRuns
            .OrderByDescending(r => r.RanAt)
            .Skip(_limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        db.ChangeRuns.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Trimmed {Count} change run(s) beyond the configured limit of {Limit}", stale.Count, _limit);
    }
}
