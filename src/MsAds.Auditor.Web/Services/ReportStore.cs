using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Reports;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Keeps generated reports so the analysis pages and the exports can read the same report
/// without re-querying the API, and so the report history has something to list.
/// </summary>
public interface IReportStore
{
    void Add(AuditReport report);

    AuditReport? Get(Guid reportId);

    /// <summary>The most recently generated report, or null if none has been generated yet.</summary>
    AuditReport? Latest { get; }

    /// <summary>All retained reports, newest first.</summary>
    IReadOnlyList<AuditReport> All { get; }

    void Clear();
}

/// <summary>
/// In-memory store, bounded to the configured limit. Deliberately not persisted: the brief asks
/// for the minimum data necessary, and keeping client performance data only in process memory
/// means nothing is written to disk and a restart clears it.
/// </summary>
public sealed class InMemoryReportStore(IOptions<AuditorOptions> options) : IReportStore
{
    private readonly ConcurrentDictionary<Guid, AuditReport> _reports = new();
    private readonly object _trimLock = new();
    private readonly int _limit = Math.Max(1, options.Value.ReportHistoryLimit);

    public void Add(AuditReport report)
    {
        _reports[report.ReportId] = report;

        // Drop the oldest beyond the limit so memory cannot grow without bound.
        lock (_trimLock)
        {
            foreach (var stale in _reports.Values
                .OrderByDescending(r => r.GeneratedAt)
                .Skip(_limit)
                .ToList())
            {
                _reports.TryRemove(stale.ReportId, out _);
            }
        }
    }

    public AuditReport? Get(Guid reportId) => _reports.GetValueOrDefault(reportId);

    public AuditReport? Latest => All.FirstOrDefault();

    public IReadOnlyList<AuditReport> All => [.. _reports.Values.OrderByDescending(r => r.GeneratedAt)];

    public void Clear() => _reports.Clear();
}
