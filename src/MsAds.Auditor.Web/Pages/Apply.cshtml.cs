using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

/// <summary>
/// Review the changes an audit suggests and choose which to apply.
/// </summary>
/// <remarks>
/// Three deliberate steps: pick, review exactly what each one will do, then confirm. Nothing is
/// pre-selected and nothing is applied without the confirmation box being ticked, because this
/// is the one place the application can alter a live account.
/// </remarks>
public class ApplyModel(
    IReportStore store,
    IOptions<AuditorOptions> options,
    ChangeOptions changeOptions,
    IChangeApplier applier,
    IChangeLog changeLog,
    ILogger<ApplyModel> logger) : ReportPageModel(store, options)
{
    public ChangeOptions Changes { get; } = changeOptions;

    public IChangeApplier Applier { get; } = applier;

    public IReadOnlyList<ProposedChange> Proposals { get; private set; } = [];

    /// <summary>The proposals chosen for the review step.</summary>
    public IReadOnlyList<ProposedChange> ForReview { get; private set; } = [];

    /// <summary>Outcome of a preview or an apply run.</summary>
    public ChangeRunResult? Result { get; private set; }

    public IReadOnlyList<ChangeRunResult> History => changeLog.All;

    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Proposals hidden because they have already been applied to this account in this session.
    /// Surfaced so the list shrinking is explained rather than mysterious.
    /// </summary>
    public int AlreadyAppliedCount { get; private set; }

    [BindProperty]
    public List<string> SelectedChangeIds { get; set; } = [];

    /// <summary>Must be ticked on the confirmation step before anything is sent.</summary>
    [BindProperty]
    public bool Confirmed { get; set; }

    public void OnGet()
    {
        LoadReport();
        LoadProposals();
    }

    /// <summary>Step two: show exactly what the selected changes would do, without sending them.</summary>
    public async Task<IActionResult> OnPostReviewAsync(CancellationToken cancellationToken)
    {
        LoadReport();
        LoadProposals();

        var selected = Selected();

        if (selected.Count == 0)
        {
            ErrorMessage = "Select at least one change to review.";
            return Page();
        }

        ForReview = selected;

        try
        {
            // A preview never sends anything; it reports the call each change would make.
            Result = await Applier
                .ApplyAsync(selected, CurrentUser(), preview: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Previewing changes failed");
            ErrorMessage = "The preview could not be produced.";
        }

        return Page();
    }

    /// <summary>Step three: send the confirmed changes.</summary>
    public async Task<IActionResult> OnPostApplyAsync(CancellationToken cancellationToken)
    {
        LoadReport();
        LoadProposals();

        var selected = Selected();
        ForReview = selected;

        if (!Changes.Enabled || !Applier.CanApply)
        {
            ErrorMessage = "Applying changes is switched off.";
            return Page();
        }

        if (selected.Count == 0)
        {
            ErrorMessage = "No changes were selected.";
            return Page();
        }

        if (!Confirmed)
        {
            ErrorMessage = "Tick the confirmation box before applying.";
            return Page();
        }

        if (selected.Count > Changes.MaxChangesPerRun)
        {
            ErrorMessage =
                $"{selected.Count} changes were selected, above the limit of {Changes.MaxChangesPerRun} "
                + "for a single run. Apply them in smaller batches.";
            return Page();
        }

        // Anything the configuration forbids is refused here as well as in the interface.
        var refused = selected.Where(c => !Changes.Permits(c)).ToList();

        if (refused.Count > 0)
        {
            ErrorMessage =
                $"{refused.Count} of the selected changes are not permitted by the current configuration.";
            return Page();
        }

        try
        {
            Result = await Applier
                .ApplyAsync(selected, CurrentUser(), preview: false, cancellationToken)
                .ConfigureAwait(false);

            changeLog.Record(Result);

            logger.LogWarning(
                "Change run {RunId} by {User}: {Applied} applied, {Failed} failed on account {AccountId}",
                Result.RunId, CurrentUser() ?? "unauthenticated", Result.AppliedCount, Result.FailedCount,
                Result.AccountId);

            // Applied changes drop out of the proposal list; the report behind them is now stale.
            LoadProposals();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Applying changes failed");
            ErrorMessage = "The changes could not be applied. The detail has been written to the application log.";
        }

        return Page();
    }

    private IReadOnlyList<ProposedChange> Selected()
        => [.. Proposals.Where(p => SelectedChangeIds.Contains(p.Id))];

    /// <summary>
    /// Builds the proposal list, dropping anything already applied to this account. Without this
    /// an applied change would stay on the list and could be sent a second time, because the
    /// report behind it is a snapshot taken before the change landed.
    /// </summary>
    private void LoadProposals()
    {
        if (Report is null)
        {
            Proposals = [];
            return;
        }

        var alreadyApplied = changeLog.AppliedChangeIds(Report.Account.AccountId);

        var all = ChangeProposalBuilder.Build(Report);
        Proposals = [.. all.Where(p => !alreadyApplied.Contains(p.Id))];
        AlreadyAppliedCount = all.Count - Proposals.Count;
    }

    /// <summary>The signed-in person, for the audit trail. Null when sign-in is not enabled.</summary>
    private string? CurrentUser() => User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
}
