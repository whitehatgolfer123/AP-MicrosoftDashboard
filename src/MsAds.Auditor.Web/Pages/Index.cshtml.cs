using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Ai;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Web.Charts;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

public class IndexModel(
    IReportStore store,
    IOptions<AuditorOptions> options,
    IAccountDirectory accountDirectory,
    ISettingsStore settingsStore,
    AuditService auditService,
    AiNarratorSwitch narrator,
    INarrativeStore narrativeStore,
    ILogger<IndexModel> logger) : ReportPageModel(store, options)
{
    /// <summary>Accounts the connected user can reach, offered as a picker.</summary>
    public IReadOnlyList<AdAccountInfo> AccessibleAccounts { get; private set; } = [];

    /// <summary>Set when listing accounts failed, so the manual entry box is still offered.</summary>
    public string? AccountListError { get; private set; }

    public AuditOutcome? Outcome { get; private set; }

    public IReadOnlyList<TrendChartView> Charts { get; private set; } = [];

    /// <summary>The optional AI commentary, once someone has asked for it.</summary>
    public AiNarrative? Narrative { get; private set; }

    /// <summary>Whether commentary can be requested at all.</summary>
    public bool AiAvailable => narrator.IsConfigured;

    public string? AiDisabledReason => narrator.DisabledReason;

    public string AiModel => narrator.Model;

    /// <summary>Which service the commentary would be billed to, so the cost is never a surprise.</summary>
    public string AiProviderName => narrator.Provider == MsAds.Auditor.Ai.AiProvider.OpenAi
        ? "OpenAI"
        : "Anthropic";

    public string SourceName => auditService.SourceName;

    public bool IsDemoSource => auditService.SourceKind == DataSourceKind.Demo;

    [BindProperty]
    public ReportForm Form { get; set; } = new();

    /// <summary>An account id arriving from the portfolio scan's "Audit" link.</summary>
    [BindProperty(SupportsGet = true, Name = "account")]
    public long? AccountFromScan { get; set; }

    /// <summary>The reporting controls, bound from the form.</summary>
    public sealed class ReportForm
    {
        /// <summary>
        /// Bound as text rather than a number so that a mistyped value produces a clear message
        /// instead of a framework-level model error.
        /// </summary>
        public string? AccountId { get; set; }

        public ReportingPeriod Period { get; set; } = ReportingPeriod.Last30Days;

        public bool CompareWithPreviousPeriod { get; set; } = true;

        public bool ExcludeToday { get; set; } = true;

        public bool AnalyseCampaigns { get; set; } = true;

        public bool AnalyseSearchTerms { get; set; } = true;

        public bool AnalyseKeywords { get; set; } = true;

        public bool AnalyseChangeHistory { get; set; } = true;

        public bool AnalyseNegativeKeywordConflicts { get; set; } = true;

        public AnalysisSelection ToSelection() => new()
        {
            Campaigns = AnalyseCampaigns,
            SearchTerms = AnalyseSearchTerms,
            Keywords = AnalyseKeywords,
            ChangeHistory = AnalyseChangeHistory,
            NegativeKeywordConflicts = AnalyseNegativeKeywordConflicts,
        };
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        LoadReport();
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);

        // Pre-select whatever the last report used, so re-running is one click.
        if (Report is not null)
        {
            Form = new ReportForm
            {
                AccountId = Report.Account.AccountId.ToString(),
                Period = Report.Period.Period,
                CompareWithPreviousPeriod = Report.Period.HasComparison,
                ExcludeToday = Report.Period.CurrentDayExcluded,
                AnalyseCampaigns = Report.Selection.Campaigns,
                AnalyseSearchTerms = Report.Selection.SearchTerms,
                AnalyseKeywords = Report.Selection.Keywords,
                AnalyseChangeHistory = Report.Selection.ChangeHistory,
                AnalyseNegativeKeywordConflicts = Report.Selection.NegativeKeywordConflicts,
            };
        }
        else if (AccessibleAccounts.Count == 1)
        {
            Form.AccountId = AccessibleAccounts[0].AccountId.ToString();
        }

        // An account chosen on the scan page wins, so following "Audit" lands ready to run.
        if (AccountFromScan is { } fromScan && fromScan > 0)
        {
            Form.AccountId = fromScan.ToString();
        }

        BuildCharts();
        LoadNarrative();
    }

    /// <summary>
    /// Writes commentary for the report on screen. A separate action from generating the report
    /// because it costs money per call, so it is never incurred without being asked for.
    /// </summary>
    public async Task<IActionResult> OnPostNarrateAsync(CancellationToken cancellationToken)
    {
        LoadReport();
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);
        BuildCharts();

        if (Report is null)
        {
            return Page();
        }

        var narrative = await narrator.NarrateAsync(Report, cancellationToken).ConfigureAwait(false);

        if (narrative is not null)
        {
            narrativeStore.Set(Report.ReportId, narrative);
            Narrative = narrative;
        }

        return Page();
    }

    private void LoadNarrative()
        => Narrative = Report is null ? null : narrativeStore.Get(Report.ReportId);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadAccountsAsync(cancellationToken).ConfigureAwait(false);

        var raw = (Form.AccountId ?? string.Empty).Trim();

        // Accept the account number form people copy from the interface, e.g. "X0001001", by
        // matching it against the accounts we can see.
        if (!long.TryParse(raw, out var accountId))
        {
            var matched = AccessibleAccounts.FirstOrDefault(a =>
                string.Equals(a.AccountNumber, raw, StringComparison.OrdinalIgnoreCase));

            if (matched is not null)
            {
                accountId = matched.AccountId;
            }
            else
            {
                Outcome = AuditOutcome.Failure(
                    "That account ID is not valid",
                    $"“{raw}” is not a Microsoft Advertising account ID. Account IDs are numeric, for example "
                    + "140100001. An account number such as X0001001 also works if the account is listed above.",
                    "Check the value, or pick an account from the list.",
                    accessFailure: AccountAccessFailure.InvalidFormat);

                LoadReport();
                BuildCharts();
                return Page();
            }
        }

        var request = new AuditRequest
        {
            AccountId = accountId,
            Period = Form.Period,
            CompareWithPreviousPeriod = Form.CompareWithPreviousPeriod,
            ExcludeToday = Form.ExcludeToday,
            Selection = Form.ToSelection(),
            Settings = settingsStore.Current,
        };

        Outcome = await auditService.RunAsync(request, cancellationToken).ConfigureAwait(false);

        if (Outcome.Succeeded)
        {
            // Redirect so the new report has a shareable, refreshable URL.
            return RedirectToPage("/Index", new { report = Outcome.Report!.ReportId });
        }

        LoadReport();
        BuildCharts();
        return Page();
    }

    private async Task LoadAccountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            AccessibleAccounts = await accountDirectory
                .ListAccessibleAccountsAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Accessible accounts could not be listed");

            AccessibleAccounts = [];
            AccountListError =
                "The list of accessible accounts could not be retrieved. You can still enter an account ID "
                + "manually below.";
        }
    }

    private void BuildCharts()
        => Charts = Report is null ? [] : TrendChartView.ForDashboard(Report);
}
