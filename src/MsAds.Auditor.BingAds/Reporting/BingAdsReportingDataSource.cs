using System.ServiceModel;
using Microsoft.BingAds.V13.Reporting;
using Microsoft.Extensions.Logging;
using MsAds.Auditor.BingAds.Authentication;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Time;

namespace MsAds.Auditor.BingAds.Reporting;

/// <summary>
/// Retrieves live data through the Microsoft Advertising Reporting API.
/// </summary>
/// <remarks>
/// <para>
/// Every dataset is a separate asynchronous report: the request is submitted, polled until it
/// completes, downloaded and parsed. A failure in one report degrades that section only — it
/// returns an unavailable dataset carrying an explanation, and the rest of the audit still runs.
/// </para>
/// <para>
/// Read-only: this type only submits report requests. It cannot change anything in an account.
/// </para>
/// </remarks>
public sealed class BingAdsReportingDataSource(
    BingAdsAuthenticationService authentication,
    ILogger<BingAdsReportingDataSource> logger) : IAdsDataSource, IDisposable
{
    /// <summary>Format version 2.0 returns undecorated values, which parse predictably.</summary>
    private const string FormatVersion = "2.0";

    /// <summary>Reports are downloaded here and deleted immediately after parsing.</summary>
    private readonly string _workingDirectory = Directory.CreateTempSubdirectory("msads-auditor-").FullName;

    /// <summary>
    /// Report types the sandbox never returns data for. It accepts the request and completes the
    /// job, but sets no download URL, so the result is indistinguishable from a genuinely empty
    /// account unless it is called out.
    /// </summary>
    private static readonly string[] UnsupportedInSandbox =
    [
        "search query performance", "change history", "negative keyword conflicts",
    ];

    private bool IsSandbox => authentication.Environment == Microsoft.BingAds.ApiEnvironment.Sandbox;

    /// <summary>
    /// Account time zones whose name differs between the customer management and reporting
    /// enumerations, so a straight parse fails.
    /// </summary>
    /// <remarks>
    /// Microsoft's two enumerations disagree on three of their seventy-five values, apparently
    /// from a careless substitution in the reporting one — <c>Islamabad</c> became
    /// <c>Islandamabad</c> and <c>Istanbul</c> became <c>Islandanbul</c>. Without these aliases
    /// an account in one of those zones would silently fall back to the service default and
    /// report on slightly the wrong days.
    /// </remarks>
    private static readonly Dictionary<string, ReportTimeZone> TimeZoneAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SolomonIslandNewCaledonia"] = ReportTimeZone.MagadanSolomonIslandNewCaledonia,
            ["IslamabadKarachiTashkent"] = ReportTimeZone.IslandamabadKarachiTashkent,
            ["AthensBuckarestIstanbul"] = ReportTimeZone.AthensIslandanbulMinsk,
        };

    /// <summary>
    /// Resolves the reporting time zone for an account, or null when it cannot be mapped.
    /// </summary>
    internal static ReportTimeZone? ResolveReportTimeZone(string? accountTimeZone)
    {
        if (string.IsNullOrWhiteSpace(accountTimeZone))
        {
            return null;
        }

        if (TimeZoneAliases.TryGetValue(accountTimeZone, out var alias))
        {
            return alias;
        }

        return Enum.TryParse<ReportTimeZone>(accountTimeZone, ignoreCase: true, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Explains what sandbox will and will not return, so an empty report there is understood
    /// rather than mistaken for a finding.
    /// </summary>
    private IEnumerable<DataNotice> SandboxNotices(string description, ReportScope scope)
    {
        if (!IsSandbox)
        {
            yield break;
        }

        if (UnsupportedInSandbox.Contains(description))
        {
            yield return DataNotice.Warning(
                "sandbox-report-unsupported",
                $"The sandbox does not return data for the {description} report. The request succeeds and "
                + "the job completes, but no results are produced. This is a limitation of the test "
                + "environment, not a finding about the account.",
                description);

            yield break;
        }

        yield return DataNotice.Info(
            "sandbox-same-day-data-only",
            "The sandbox generates test performance data only for the day an entity was added or "
            + "updated, and only a few hours afterwards. A reporting period that excludes today will "
            + "therefore come back empty — untick \u201CExclude today\u201D when testing against sandbox.",
            description);
    }

    public DataSourceKind Kind => DataSourceKind.Live;

    public string DisplayName => "Microsoft Advertising API";

    public Task<DataSet<DailyMetrics>> GetDailyMetricsAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
        => RunAsync(
            "daily account performance",
            scope,
            () => new AccountPerformanceReportRequest
            {
                FormatVersion = FormatVersion,
                ReportName = "Account performance (daily)",
                Aggregation = ReportAggregation.Daily,
                ReturnOnlyCompleteData = false,
                Columns = ReportColumns.AccountDaily,
                Scope = new AccountReportScope { AccountIds = [scope.AccountId] },
                Time = BuildTime(scope),
            },
            ReportRowMapper.Daily,
            cancellationToken);

    public Task<DataSet<CampaignPerformance>> GetCampaignPerformanceAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
        => RunAsync(
            "campaign performance",
            scope,
            () => new CampaignPerformanceReportRequest
            {
                FormatVersion = FormatVersion,
                ReportName = "Campaign performance",
                Aggregation = ReportAggregation.Summary,
                ReturnOnlyCompleteData = false,
                Columns = ReportColumns.Campaign,
                Scope = new AccountThroughCampaignReportScope { AccountIds = [scope.AccountId] },
                Time = BuildTime(scope),
            },
            row => ReportRowMapper.Campaign(row),
            cancellationToken);

    public Task<DataSet<SearchTermPerformance>> GetSearchTermsAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
        => RunAsync(
            "search query performance",
            scope,
            () => new SearchQueryPerformanceReportRequest
            {
                FormatVersion = FormatVersion,
                ReportName = "Search query performance",
                Aggregation = ReportAggregation.Summary,
                ReturnOnlyCompleteData = false,
                Columns = ReportColumns.SearchQuery,
                Scope = new AccountThroughAdGroupReportScope { AccountIds = [scope.AccountId] },
                Time = BuildTime(scope),
            },
            ReportRowMapper.SearchTerm,
            cancellationToken,
            // Microsoft never publishes every query, so this caveat travels with the data.
            extraNotices: [DataNotices.SearchTermCoverage()]);

    public Task<DataSet<KeywordPerformance>> GetKeywordPerformanceAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
        => RunAsync(
            "keyword performance",
            scope,
            () => new KeywordPerformanceReportRequest
            {
                FormatVersion = FormatVersion,
                ReportName = "Keyword performance",
                Aggregation = ReportAggregation.Summary,
                ReturnOnlyCompleteData = false,
                Columns = ReportColumns.Keyword,
                Scope = new AccountThroughAdGroupReportScope { AccountIds = [scope.AccountId] },
                Time = BuildTime(scope),
            },
            row => ReportRowMapper.Keyword(row),
            cancellationToken);

    public Task<DataSet<ChangeRecord>> GetChangeHistoryAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
        => RunAsync(
            "change history",
            scope,
            () => new SearchCampaignChangeHistoryReportRequest
            {
                FormatVersion = FormatVersion,
                ReportName = "Change history",
                Columns = ReportColumns.ChangeHistory,
                Scope = new AccountThroughAdGroupReportScope { AccountIds = [scope.AccountId] },
                Time = BuildTime(scope),
            },
            ReportRowMapper.Change,
            cancellationToken);

    public Task<DataSet<NegativeKeywordConflict>> GetNegativeKeywordConflictsAsync(
        ReportScope scope, CancellationToken cancellationToken = default)
        => RunAsync(
            "negative keyword conflicts",
            scope,
            // A current-state report: it takes no date range.
            () => new NegativeKeywordConflictReportRequest
            {
                FormatVersion = FormatVersion,
                ReportName = "Negative keyword conflicts",
                Columns = ReportColumns.NegativeKeywordConflict,
                Scope = new AccountThroughAdGroupReportScope { AccountIds = [scope.AccountId] },
            },
            ReportRowMapper.Conflict,
            cancellationToken);

    /// <summary>
    /// Submits a report, waits for it, and maps the rows. Any failure becomes an unavailable
    /// dataset with an explanation rather than an exception, so one broken report cannot take
    /// the whole audit down.
    /// </summary>
    private async Task<DataSet<T>> RunAsync<T>(
        string description,
        ReportScope scope,
        Func<ReportRequest> buildRequest,
        Func<IReportRow, T?> map,
        CancellationToken cancellationToken,
        IReadOnlyList<DataNotice>? extraNotices = null)
        where T : class
    {
        try
        {
            var authorization = await authentication
                .GetAuthorizationDataAsync(scope.AccountId, scope.CustomerId, cancellationToken)
                .ConfigureAwait(false);

            var manager = new ReportingServiceManager(authorization, authentication.Environment)
            {
                WorkingDirectory = _workingDirectory,
                StatusPollIntervalInMilliseconds = 5000,
            };

            var parameters = new ReportingDownloadParameters
            {
                ReportRequest = buildRequest(),
                ResultFileDirectory = _workingDirectory,
                ResultFileName = $"{Guid.NewGuid():N}.csv",
                OverwriteResultFile = true,
            };

            logger.LogInformation(
                "Requesting {Report} for account {AccountId} over {Range}",
                description, scope.AccountId, scope.Range);

            using var report = await manager
                .DownloadReportAsync(parameters, cancellationToken)
                .ConfigureAwait(false);

            // A report with no matching data comes back as null rather than as an empty report.
            if (report is null)
            {
                logger.LogInformation("{Report} returned no rows for account {AccountId}", description, scope.AccountId);

                return new DataSet<T>
                {
                    Rows = [],
                    Notices = [.. extraNotices ?? [], .. SandboxNotices(description, scope)],
                };
            }

            var columns = new HashSet<string>(report.ReportColumns ?? [], StringComparer.OrdinalIgnoreCase);
            var rows = new List<T>();

            foreach (var record in report.GetReportRecords())
            {
                if (map(new ReportRecordRow(record, columns)) is { } mapped)
                {
                    rows.Add(mapped);
                }
            }

            var notices = new List<DataNotice>(extraNotices ?? []);
            notices.AddRange(SandboxNotices(description, scope));

            if (scope.ReportingTimeZone is { Length: > 0 } reported
                && ResolveReportTimeZone(reported) is null)
            {
                notices.Add(DataNotice.Warning(
                    "timezone-not-mapped",
                    $"The account's reporting time zone ({reported}) is not one the Reporting API "
                    + "recognises, so the service applied its own default. The day boundaries in this "
                    + "report may not match the account's own calendar.",
                    description));
            }

            if (report.PotentialIncompleteData)
            {
                notices.Add(DataNotice.Warning(
                    "potentially-incomplete",
                    $"Microsoft Advertising flagged the {description} report as potentially incomplete. Figures "
                    + "for the most recent days may still be restated.",
                    description));
            }

            logger.LogInformation(
                "{Report} returned {Rows} usable row(s) for account {AccountId}",
                description, rows.Count, scope.AccountId);

            return new DataSet<T> { Rows = rows, Notices = notices };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BingAdsNotConnectedException ex)
        {
            return DataSet<T>.Unavailable(DataNotice.Error("not-connected", ex.Message, description));
        }
        catch (Exception ex)
        {
            // Full detail to the log; a usable summary to the interface.
            logger.LogError(ex, "The {Report} report failed for account {AccountId}", description, scope.AccountId);

            return DataSet<T>.Unavailable(DataNotice.Error(
                "report-failed",
                $"The {description} report could not be retrieved: {Describe(ex)} This section is therefore "
                + "unavailable — it is not a statement that there is nothing to report.",
                description));
        }
        finally
        {
            CleanWorkingDirectory();
        }
    }

    /// <summary>Turns an SDK or transport failure into something a user can act on.</summary>
    private static string Describe(Exception ex) => ex switch
    {
        ReportingOperationCouldNotBeCompletedException => "Microsoft Advertising reported that the job "
            + "failed. This is often transient, or the reporting period may be too large for this report.",
        ReportingOperationInProgressException => "the report was still generating when the wait expired. "
            + "Try again, or choose a shorter reporting period.",
        TimeoutException => "the request timed out. Try a shorter reporting period.",
        HttpRequestException => "Microsoft Advertising could not be reached.",
        FaultException => "Microsoft Advertising rejected the request. Check that the signed-in user still "
            + "has access to this account, and that the developer token is valid.",
        _ => "an unexpected error occurred, and the detail has been written to the application log.",
    };

    /// <summary>
    /// Builds the report's date range, and pins it to the account's own reporting time zone so
    /// the day boundaries match the dates the period was resolved against.
    /// </summary>
    private static ReportTime BuildTime(ReportScope scope)
    {
        var time = new ReportTime
        {
            CustomDateRangeStart = ToReportDate(scope.Range.Start),
            CustomDateRangeEnd = ToReportDate(scope.Range.End),
        };

        if (ResolveReportTimeZone(scope.ReportingTimeZone) is { } zone)
        {
            time.ReportTimeZone = zone;
        }

        return time;
    }

    private static Date ToReportDate(DateOnly date)
        => new() { Year = date.Year, Month = date.Month, Day = date.Day };

    /// <summary>
    /// Deletes the downloaded report files. Client performance data must not be left sitting in
    /// a temporary directory after the report has been parsed.
    /// </summary>
    private void CleanWorkingDirectory()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_workingDirectory))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Temporary report files could not be removed: {Reason}", ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        CleanWorkingDirectory();

        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch (Exception)
        {
            // Nothing useful to do at shutdown.
        }
    }
}
