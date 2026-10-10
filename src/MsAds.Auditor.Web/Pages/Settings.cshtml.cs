using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Core.Ai;
using MsAds.Auditor.Core.Rules;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

public class SettingsModel(
    IOptions<AuditorOptions> options,
    ISettingsStore settingsStore,
    RecommendationEngine engine,
    ChangeOptions changeOptions,
    MsAds.Auditor.Ai.IAiOptionsAccessor aiOptions,
    IAiCredentialStore aiCredentials,
    AiNarratorSwitch narrator,
    IAdsDataSource dataSource) : PageModel
{
    public AuditorOptions Options { get; } = options.Value;

    public IReadOnlyList<IAuditRule> Rules => engine.Rules;

    public bool IsDemo => dataSource.Kind == DataSourceKind.Demo;

    /// <summary>Whether this instance may alter advertising accounts, and how far.</summary>
    public ChangeOptions Changes { get; } = changeOptions;

    /// <summary>The optional AI commentary configuration currently in force.</summary>
    public MsAds.Auditor.Ai.AiOptions Ai => aiOptions.Current;

    public bool AiConfigured => narrator.IsConfigured;

    /// <summary>Rough per-report cost, so the spend is visible before anyone opts in.</summary>
    public decimal? AiTypicalCostUsd => narrator.TypicalCostUsd;

    /// <summary>
    /// The saved key's provider and last four characters, or null when the key comes from
    /// configuration or there is none. The key itself is never sent to the browser.
    /// </summary>
    public AiCredential? SavedAiCredential => aiCredentials.Read();

    /// <summary>Set after the AI key is saved or removed, for the confirmation message.</summary>
    public string? AiMessage { get; private set; }

    public string? AiError { get; private set; }

    public string SourceName => dataSource.DisplayName;

    /// <summary>Set after a successful save.</summary>
    public bool Saved { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    [BindProperty]
    public AuditSettings Settings { get; set; } = new();

    public void OnGet() => Settings = settingsStore.Current;

    public IActionResult OnPost()
    {
        var errors = Settings.Validate();

        if (errors.Count > 0)
        {
            Errors = errors;
            return Page();
        }

        settingsStore.Update(Settings);
        Saved = true;
        return Page();
    }

    /// <summary>
    /// Saves an AI API key entered on this page. The key is encrypted before it touches the disk
    /// and is never read back out to the browser.
    /// </summary>
    public IActionResult OnPostAi(string? aiProvider, string? aiApiKey, string? aiModel)
    {
        Settings = settingsStore.Current;

        var key = aiApiKey?.Trim();

        if (string.IsNullOrEmpty(key))
        {
            AiError = "Enter an API key, or use Remove key to switch the commentary off.";
            return Page();
        }

        if (!Enum.TryParse<MsAds.Auditor.Ai.AiProvider>(aiProvider, ignoreCase: true, out var provider))
        {
            AiError = "Choose which service the key belongs to.";
            return Page();
        }

        // A key for the wrong provider fails on the first call with an opaque error, so check the
        // shape here where it can be explained.
        if (provider == MsAds.Auditor.Ai.AiProvider.OpenAi && !key.StartsWith("sk-", StringComparison.Ordinal))
        {
            AiError = "That does not look like an OpenAI key — they begin \"sk-\". "
                + "Check the provider selected above.";
            return Page();
        }

        if (provider == MsAds.Auditor.Ai.AiProvider.Anthropic
            && !key.StartsWith("sk-ant-", StringComparison.Ordinal))
        {
            AiError = "That does not look like an Anthropic key — they begin \"sk-ant-\". "
                + "Check the provider selected above.";
            return Page();
        }

        aiCredentials.Write(new AiCredential
        {
            Provider = provider,
            ApiKey = key,
            Model = string.IsNullOrWhiteSpace(aiModel) ? null : aiModel.Trim(),
        });

        AiMessage = $"Key saved for {provider}. Commentary is now available on reports using "
            + $"{Ai.EffectiveModel}. Nothing is spent until you ask for one.";

        return Page();
    }

    /// <summary>Removes the saved key, switching the commentary off again.</summary>
    public IActionResult OnPostRemoveAi()
    {
        Settings = settingsStore.Current;
        aiCredentials.Clear();

        AiMessage = aiOptions.Current.IsConfigured
            ? "The saved key was removed. A key is still set in configuration, so commentary "
              + "remains available."
            : "The saved key was removed. AI commentary is off; the audit is unaffected.";

        return Page();
    }

    public IActionResult OnPostReset()
    {
        settingsStore.ResetToDefaults();
        Settings = settingsStore.Current;
        Saved = true;
        return Page();
    }
}
