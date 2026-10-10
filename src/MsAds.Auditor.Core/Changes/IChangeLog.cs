namespace MsAds.Auditor.Core.Changes;

/// <summary>
/// The record of every apply run, so there is an answer to "what did this tool change, and when".
/// </summary>
public interface IChangeLog
{
    void Record(ChangeRunResult result);

    /// <summary>Runs most recent first.</summary>
    IReadOnlyList<ChangeRunResult> All { get; }

    /// <summary>
    /// Change ids already applied to an account. Used to drop them from the proposal list, so a
    /// change cannot be applied twice from the same report.
    /// </summary>
    IReadOnlySet<string> AppliedChangeIds(long accountId);
}

/// <summary>
/// In-memory log, bounded. Enough to answer the question within a session; a deployment that
/// needs a permanent audit trail should persist this.
/// </summary>
public sealed class InMemoryChangeLog(int limit = 100) : IChangeLog
{
    private readonly List<ChangeRunResult> _runs = [];
    private readonly Lock _gate = new();

    public void Record(ChangeRunResult result)
    {
        lock (_gate)
        {
            _runs.Add(result);

            if (_runs.Count > limit)
            {
                _runs.RemoveRange(0, _runs.Count - limit);
            }
        }
    }

    public IReadOnlySet<string> AppliedChangeIds(long accountId)
    {
        lock (_gate)
        {
            return _runs
                .Where(r => !r.WasPreview && r.AccountId == accountId)
                .SelectMany(r => r.Outcomes)
                .Where(o => o.Status == ChangeStatus.Applied)
                .Select(o => o.Change.Id)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    public IReadOnlyList<ChangeRunResult> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _runs.OrderByDescending(r => r.RanAt)];
            }
        }
    }
}
