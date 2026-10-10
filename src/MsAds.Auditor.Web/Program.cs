using Microsoft.Extensions.Options;
using MsAds.Auditor.BingAds;
using MsAds.Auditor.BingAds.Authentication;
using MsAds.Auditor.BingAds.CampaignManagement;
using MsAds.Auditor.BingAds.Reporting;
using MsAds.Auditor.Core.Abstractions;
using MsAds.Auditor.Ai;
using MsAds.Auditor.Core.Ai;
using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Core.Demo;
using MsAds.Auditor.Core.DependencyInjection;
using MsAds.Auditor.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// The OAuth state parameter is held across the redirect, so a forged callback can be rejected.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Who may open the application. Bound eagerly because both the service registration and the
// start-up safety check need it before the container is built.
var signIn = builder.Configuration.GetSection(AppSignInOptions.SectionName).Get<AppSignInOptions>()
             ?? new AppSignInOptions();

builder.Services.AddSingleton(signIn);
builder.Services.AddAppSignIn(signIn);

// Refuse to listen somewhere reachable while anonymous. Kestrel's own default is loopback, so
// this only trips when the addresses have been changed deliberately.
NetworkExposureGuard.Validate(
    (builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? string.Empty)
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
    signIn);

builder.Services
    .AddOptions<AuditorOptions>()
    .Bind(builder.Configuration.GetSection(AuditorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Bound separately so the credentials can be injected on their own, without handing the whole
// application configuration to the API layer.
builder.Services
    .AddOptions<MicrosoftAdvertisingOptions>()
    .Bind(builder.Configuration.GetSection($"{AuditorOptions.SectionName}:MicrosoftAdvertising"));

// Metric calculations, analysis and the rules engine.
builder.Services.AddAuditorCore();

// --- Microsoft Advertising integration -------------------------------------------------------
builder.Services.AddDataProtection();

// The refresh token is a long-lived credential. It is kept in memory unless persistence is
// explicitly enabled, in which case it is encrypted at rest with Data Protection.
builder.Services.AddSingleton<IRefreshTokenStore>(provider =>
{
    var options = provider.GetRequiredService<IOptions<MicrosoftAdvertisingOptions>>().Value;

    return options.PersistRefreshToken
        ? ActivatorUtilities.CreateInstance<ProtectedFileRefreshTokenStore>(provider)
        : new InMemoryRefreshTokenStore();
});

builder.Services.AddSingleton<BingAdsAuthenticationService>();
builder.Services.AddSingleton<BingAdsAccountDirectory>();
builder.Services.AddSingleton<BingAdsReportingDataSource>();

// --- Data source selection -------------------------------------------------------------------
// Chosen by configuration. Demo is the default so a fresh checkout runs with no credentials, and
// live mode never silently falls back to synthetic figures.
builder.Services.AddSingleton<DemoDataSource>();
builder.Services.AddSingleton<DemoAccountDirectory>();

builder.Services.AddSingleton<IAdsDataSource>(provider =>
    provider.GetRequiredService<IOptions<AuditorOptions>>().Value.Mode == AuditorMode.Live
        ? provider.GetRequiredService<BingAdsReportingDataSource>()
        : provider.GetRequiredService<DemoDataSource>());

builder.Services.AddSingleton<IAccountDirectory>(provider =>
    provider.GetRequiredService<IOptions<AuditorOptions>>().Value.Mode == AuditorMode.Live
        ? provider.GetRequiredService<BingAdsAccountDirectory>()
        : provider.GetRequiredService<DemoAccountDirectory>());

// --- Applying changes -----------------------------------------------------------------------
// Off unless deliberately configured. While off, the applier registered refuses everything, so
// the read-only guarantee holds even if a request somehow reaches it.
var changeOptions = builder.Configuration.GetSection(ChangeOptions.SectionName).Get<ChangeOptions>()
                    ?? new ChangeOptions();

builder.Services.AddSingleton(changeOptions);
builder.Services.AddSingleton<IChangeLog>(new InMemoryChangeLog());
builder.Services.AddSingleton<DemoChangeApplier>();
builder.Services.AddSingleton<BingAdsChangeApplier>();

builder.Services.AddSingleton<IChangeApplier>(provider =>
{
    if (!changeOptions.Enabled)
    {
        return new DisabledChangeApplier(
            "Set Auditor:Changes:Enabled to true to allow it. The application is read-only until "
            + "you do.");
    }

    // In demo mode changes are simulated, so the approval workflow can be exercised safely.
    return provider.GetRequiredService<IOptions<AuditorOptions>>().Value.Mode == AuditorMode.Live
        ? provider.GetRequiredService<BingAdsChangeApplier>()
        : provider.GetRequiredService<DemoChangeApplier>();
});

// --- Optional AI commentary -----------------------------------------------------------------
// Entirely additive and off unless an API key is supplied. The deterministic analysis is
// identical either way; this only adds written commentary alongside it, billed by the AI
// provider per call rather than by this application.
builder.Services
    .AddOptions<AiOptions>()
    .Bind(builder.Configuration.GetSection(AiOptions.SectionName));

// Both providers are registered, and which one is used is decided per call, so the key on the
// settings page can be added, changed or switched between providers without a restart.
builder.Services.AddSingleton<IAiCredentialStore, ProtectedFileAiCredentialStore>();
builder.Services.AddSingleton<IAiOptionsAccessor, StoredAiOptionsAccessor>();
builder.Services.AddSingleton<ClaudeNarrator>();
builder.Services.AddSingleton<OpenAiNarrator>();
builder.Services.AddSingleton<AiNarratorSwitch>();
builder.Services.AddSingleton<IAiNarrator>(provider =>
    provider.GetRequiredService<AiNarratorSwitch>());

builder.Services.AddSingleton<IReportStore, InMemoryReportStore>();
builder.Services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
builder.Services.AddSingleton<IScanStore, InMemoryScanStore>();
builder.Services.AddSingleton<INarrativeStore, InMemoryNarrativeStore>();
builder.Services.AddScoped<AuditService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();

    // Only outside development: locally the app is commonly run over plain HTTP, where
    // redirecting has no port to target.
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAppSignIn(signIn);
app.UseSession();
app.MapRazorPages();

// Report the operating mode once at startup, so which data is being shown is never a surprise.
var auditorOptions = app.Services.GetRequiredService<IOptions<AuditorOptions>>().Value;
var adsOptions = app.Services.GetRequiredService<IOptions<MicrosoftAdvertisingOptions>>().Value;
var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation(
    "Microsoft Ads Performance Auditor starting in {Mode} mode. This application is read-only and "
    + "makes no changes to advertising accounts.", auditorOptions.Mode);

if (signIn.Enabled)
{
    logger.LogInformation(
        "Sign-in is required. Only accounts in directory {TenantId} may open the application{Restriction}.",
        signIn.TenantId,
        signIn.AllowedUsers.Count > 0 ? $", restricted to {signIn.AllowedUsers.Count} listed user(s)" : "");
}

if (changeOptions.Enabled)
{
    logger.LogWarning(
        "Applying changes is ENABLED{Pausing}. This application can now alter advertising accounts. "
        + "Every change still requires explicit confirmation from a person.",
        changeOptions.AllowPausing ? ", including pausing campaigns and keywords" : " for exclusions only");
}
else
{
    logger.LogInformation("Applying changes is disabled; the application is read-only.");
}

var aiOptions = app.Services.GetRequiredService<IAiOptionsAccessor>().Current;

if (aiOptions.IsConfigured)
{
    logger.LogInformation(
        "Optional AI commentary is available using {Model} via {Provider}; each report generated "
        + "with it is billed to that API key, not by this tool.",
        aiOptions.EffectiveModel,
        aiOptions.Provider);
}
else
{
    logger.LogInformation(
        "Optional AI commentary is off (no API key). The audit is unaffected.");
}

if (!signIn.Enabled)
{
    logger.LogWarning(
        "Sign-in is DISABLED: anyone who can reach this address can read and export every client's "
        + "data. This is only appropriate on localhost. Configure Auditor:SignIn before sharing it.");
}

if (auditorOptions.Mode == AuditorMode.Live)
{
    var missing = adsOptions.MissingSettings();

    if (missing.Count > 0)
    {
        logger.LogWarning(
            "Live mode is selected but these settings are missing: {Missing}. Configure them with user "
            + "secrets or environment variables — never in source control.", string.Join(", ", missing));
    }
    else
    {
        logger.LogInformation(
            "Live mode against the {Environment} environment. Sign in at /Connect to grant access.",
            adsOptions.Environment);
    }
}

app.Run();
