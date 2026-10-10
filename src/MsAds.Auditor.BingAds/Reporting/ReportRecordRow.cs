using Microsoft.BingAds.V13.Reporting;

namespace MsAds.Auditor.BingAds.Reporting;

/// <summary>
/// Adapts the SDK's report record to <see cref="IReportRow"/>.
/// </summary>
/// <remarks>
/// The SDK's accessors throw when asked for a column the report does not contain, so the set of
/// columns actually returned is checked first. That turns "the account did not return this
/// column" into a null the mapping layer already knows how to represent, instead of an exception
/// that would lose the whole report.
/// </remarks>
internal sealed class ReportRecordRow(IReportRecord record, HashSet<string> availableColumns) : IReportRow
{
    public string? Raw(string column)
    {
        if (!availableColumns.Contains(column))
        {
            return null;
        }

        try
        {
            return record.GetStringValue(column);
        }
        catch (Exception)
        {
            // A column present in the header but unreadable in this row: treat as no value
            // rather than failing the report.
            return null;
        }
    }
}
