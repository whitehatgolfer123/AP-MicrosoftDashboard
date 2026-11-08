namespace MsAds.Auditor.Core.Changes;

/// <summary>
/// The record of every apply run, so there is an answer to "what did this tool change, and when".
/// </summary>
/// <remarks>
/// <para>
/// Asynchronous throughout because the deployed implementation is database-backed: this is the
/// one record in the application that has to survive a restart, since it describes changes made
/// to client accounts. An in-memory implementation is kept for tests and for a run with
/// persistence switched off.
/// </para>
/// <para>
/// Append-only by contract. There is deliberately no method to amend or delete a recorded run.
/// </para>
/// </remarks>
public interface IChangeLog
{
    Task RecordAsync(ChangeRunResult result, CancellationToken cancellationToken = default);

    /// <summary>Runs most recent first, newest <paramref name="limit"/> only.</summary>
    Task<IReadOnlyList<ChangeRunResult>> RecentAsync(
        int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Change ids already applied to an account. Used to drop them from the proposal list, so a
    /// change cannot be applied twice from the same report.
    /// </summary>
    Task<IReadOnlySet<string>> AppliedChangeIdsAsync(
        long accountId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory log, bounded. Used by the tests and whenever persistence is switched off. A
/// deployment that needs a permanent audit trail configures the database-backed implementation
/// in <c>MsAds.Auditor.Data</c>, which is the default.
/// </summary>
public sealed class InMemoryChangeLog(int limit = 100) : IChangeLog
{
    private readonly List<ChangeRunResult> _runs = [];
    private readonly Lock _gate = new();

    public Task RecordAsync(ChangeRunResult result, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _runs.Add(result);

            if (_runs.Count > limit)
            {
                _runs.RemoveRange(0, _runs.Count - limit);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<string>> AppliedChangeIdsAsync(
        long accountId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlySet<string> applied = _runs
                .Where(r => !r.WasPreview && r.AccountId == accountId)
                .SelectMany(r => r.Outcomes)
                .Where(o => o.Status == ChangeStatus.Applied)
                .Select(o => o.Change.Id)
                .ToHashSet(StringComparer.Ordinal);

            return Task.FromResult(applied);
        }
    }

    public Task<IReadOnlyList<ChangeRunResult>> RecentAsync(
        int limit = 100, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<ChangeRunResult> recent =
                [.. _runs.OrderByDescending(r => r.RanAt).Take(Math.Max(1, limit))];

            return Task.FromResult(recent);
        }
    }
}
