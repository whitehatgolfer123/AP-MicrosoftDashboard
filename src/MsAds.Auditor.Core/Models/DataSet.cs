namespace MsAds.Auditor.Core.Models;

/// <summary>
/// Rows retrieved from a data source, together with any caveats about their completeness.
/// Carrying the notices alongside the rows means a partial result can be used without the
/// interface losing the fact that it was partial.
/// </summary>
public sealed record DataSet<T>
{
    public IReadOnlyList<T> Rows { get; init; } = [];

    public IReadOnlyList<DataNotice> Notices { get; init; } = [];

    /// <summary>
    /// False when the data source knows the result is incomplete — a truncated report, a column
    /// that could not be requested, or a partial download.
    /// </summary>
    public bool IsComplete { get; init; } = true;

    /// <summary>
    /// True when the dataset could not be retrieved at all, as opposed to being retrieved and
    /// found empty. The distinction matters: "no wasteful search terms" and "search term data
    /// unavailable" are different conclusions.
    /// </summary>
    public bool IsUnavailable { get; init; }

    public int Count => Rows.Count;

    public bool IsEmpty => Rows.Count == 0;

    public static DataSet<T> Of(IReadOnlyList<T> rows, params DataNotice[] notices)
        => new() { Rows = rows, Notices = notices };

    public static DataSet<T> Unavailable(DataNotice reason)
        => new() { Rows = [], Notices = [reason], IsComplete = false, IsUnavailable = true };

    public DataSet<T> WithNotices(params DataNotice[] extra)
        => this with { Notices = [.. Notices, .. extra] };
}
