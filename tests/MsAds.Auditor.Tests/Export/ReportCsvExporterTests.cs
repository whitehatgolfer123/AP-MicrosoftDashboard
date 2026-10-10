using MsAds.Auditor.Core.Demo;
using MsAds.Auditor.Core.Export;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Tests.Support;

namespace MsAds.Auditor.Tests.Export;

public class ReportCsvExporterTests
{
    private static async Task<AuditReport> DemoReportAsync(AuditSettings? settings = null)
    {
        var clock = new FixedClock(TestData.FixedNow);
        var period = new ReportingPeriodResolver(clock).Resolve(ReportingPeriod.Last30Days, TimeZoneInfo.Utc);

        return await TestData.Builder(clock).BuildAsync(
            new DemoDataSource(clock),
            DemoAccounts.Primary,
            period,
            settings ?? new AuditSettings { TargetCpa = 60m },
            AnalysisSelection.All);
    }

    [Fact]
    public async Task Every_advertised_export_produces_content()
    {
        var report = await DemoReportAsync();

        foreach (var (key, _) in ReportCsvExporter.Available)
        {
            var csv = ReportCsvExporter.Export(report, key);

            Assert.False(string.IsNullOrWhiteSpace(csv), $"Export '{key}' produced nothing.");
            Assert.Contains("Microsoft Ads Performance Auditor", csv);
        }
    }

    [Fact]
    public async Task An_unknown_export_key_is_rejected()
    {
        var report = await DemoReportAsync();

        Assert.Throws<ArgumentOutOfRangeException>(() => ReportCsvExporter.Export(report, "nonsense"));
    }

    [Fact]
    public async Task Exports_record_the_account_the_dates_and_the_currency()
    {
        var report = await DemoReportAsync();
        var csv = ReportCsvExporter.Campaigns(report);

        Assert.Contains(report.Account.AccountId.ToString(), csv);
        Assert.Contains("2026-02-14", csv);  // 30 days ending 15 March 2026
        Assert.Contains("2026-03-15", csv);
        Assert.Contains("Currency: GBP", csv);
    }

    [Fact]
    public async Task A_demo_report_is_labelled_as_demo_in_every_export_and_in_the_file_name()
    {
        var report = await DemoReportAsync();

        foreach (var (key, _) in ReportCsvExporter.Available)
        {
            Assert.Contains("DEMO DATA", ReportCsvExporter.Export(report, key));
            Assert.Contains("-DEMO.csv", ReportCsvExporter.FileName(report, key));
        }
    }

    [Fact]
    public async Task The_search_term_export_carries_the_coverage_warning()
    {
        var report = await DemoReportAsync();
        var csv = ReportCsvExporter.SearchTerms(report);

        Assert.Contains("does not report every search query", csv);
        Assert.Contains("Recommended action", csv);
    }

    [Fact]
    public async Task The_negative_keyword_export_states_that_nothing_is_applied_automatically()
    {
        var report = await DemoReportAsync();
        var csv = ReportCsvExporter.NegativeKeywords(report);

        // The application can now apply changes, but only on an explicit confirmation, so the
        // export says that rather than claiming it never writes.
        Assert.Contains("Nothing is applied automatically", csv);
        Assert.Contains("selects it and confirms it", csv);
    }

    [Fact]
    public async Task The_recommendations_export_includes_the_reason_action_and_caveats_for_each_finding()
    {
        var report = await DemoReportAsync();
        var csv = ReportCsvExporter.Recommendations(report);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("Reason,Suggested action,Supporting data,Caveats,Rule ID", csv);

        // Header row plus one row per finding, after the preamble lines.
        Assert.True(lines.Length >= report.Recommendations.Findings.Count + 1);
    }

    [Fact]
    public async Task Exports_quote_search_terms_containing_commas_without_breaking_the_columns()
    {
        var report = await DemoReportAsync();
        var csv = ReportCsvExporter.SearchTerms(report);

        // Parse the data section and check every row has the declared column count.
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var headerIndex = Array.FindIndex(lines, l => l.StartsWith("Search term,"));

        Assert.True(headerIndex >= 0, "Header row not found.");

        var expectedColumns = CountFields(lines[headerIndex]);

        foreach (var line in lines.Skip(headerIndex + 1))
        {
            Assert.Equal(expectedColumns, CountFields(line));
        }
    }

    /// <summary>Counts top-level comma-separated fields, respecting quoting.</summary>
    private static int CountFields(string line)
    {
        var fields = 1;
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
            }
            else if (line[i] == ',' && !inQuotes)
            {
                fields++;
            }
        }

        return fields;
    }
}
