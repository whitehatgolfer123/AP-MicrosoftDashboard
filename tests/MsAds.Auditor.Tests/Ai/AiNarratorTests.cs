using MsAds.Auditor.Ai;
using MsAds.Auditor.Core.Ai;
using MsAds.Auditor.Core.Demo;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Core.Time;
using MsAds.Auditor.Tests.Support;

namespace MsAds.Auditor.Tests.Ai;

/// <summary>
/// The optional AI commentary layer. The point of these tests is that the feature cannot change
/// the analysis: it is off by default, it only ever receives already-calculated figures, and its
/// absence costs nothing.
/// </summary>
public class AiNarratorTests
{
    private static readonly FixedClock Clock = new(TestData.FixedNow);

    private static async Task<AuditReport> DemoReportAsync(AuditSettings? settings = null)
    {
        var period = new ReportingPeriodResolver(Clock).Resolve(ReportingPeriod.Last30Days, TimeZoneInfo.Utc);

        return await TestData.Builder(Clock).BuildAsync(
            new DemoDataSource(Clock),
            DemoAccounts.Primary,
            period,
            settings ?? new AuditSettings { TargetCpa = 60m },
            AnalysisSelection.All);
    }

    [Fact]
    public void Commentary_is_off_until_an_api_key_is_supplied()
    {
        var options = new AiOptions();

        Assert.False(options.IsConfigured);
        Assert.Null(options.Model);
        Assert.False(options.Automatic);

        // Nothing is ever charged without being asked for, even once a key exists.
        Assert.False(new AiOptions { ApiKey = "test" }.Automatic);
    }

    [Fact]
    public async Task The_disabled_narrator_returns_nothing_and_never_throws()
    {
        var narrator = new DisabledAiNarrator("No key.");

        Assert.False(narrator.IsConfigured);
        Assert.Null(await narrator.NarrateAsync(await DemoReportAsync()));
    }

    [Fact]
    public async Task The_brief_carries_the_calculated_figures_rather_than_raw_data()
    {
        var report = await DemoReportAsync();
        var brief = AiPromptBuilder.BuildBrief(report);

        // Already-computed values, so the model never has arithmetic to do.
        Assert.Contains("Total spend:", brief);
        Assert.Contains("Cost per acquisition:", brief);
        Assert.Contains(report.ExecutiveSummary.Headline, brief);
        Assert.Contains(report.Account.AccountName, brief);
        Assert.Contains(report.DateRangeLabel, brief);
    }

    [Fact]
    public async Task The_brief_states_which_direction_is_favourable_for_each_metric()
    {
        var brief = AiPromptBuilder.BuildBrief(await DemoReportAsync());

        // Polarity is supplied so the model cannot decide that rising spend is good news.
        Assert.Contains("neither good nor bad on its own", brief);
        Assert.Contains("adverse", brief);
    }

    [Fact]
    public async Task The_brief_passes_through_confidence_so_uncertainty_is_not_firmed_up()
    {
        var brief = AiPromptBuilder.BuildBrief(await DemoReportAsync());

        Assert.Contains("confidence]", brief);
    }

    [Fact]
    public async Task The_brief_warns_the_model_off_conversions_when_that_data_is_missing()
    {
        // Account 140100008 in the demo set reports no conversion columns at all.
        var period = new ReportingPeriodResolver(Clock).Resolve(ReportingPeriod.Last30Days, TimeZoneInfo.Utc);
        var untracked = DemoAccounts.All.Single(a => a.AccountId == 140100008);

        var report = await TestData.Builder(Clock).BuildAsync(
            new DemoDataSource(Clock), untracked, period, new AuditSettings(), AnalysisSelection.All);

        var brief = AiPromptBuilder.BuildBrief(report);

        Assert.True(report.ConversionDataUnavailable);
        Assert.Contains("conversion data was unavailable", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not draw any conclusion", brief);
    }

    [Fact]
    public async Task A_demo_report_is_flagged_as_synthetic_in_the_brief()
        => Assert.Contains("synthetic demo figures", AiPromptBuilder.BuildBrief(await DemoReportAsync()));

    [Fact]
    public async Task The_brief_repeats_the_search_term_coverage_limit()
    {
        var brief = AiPromptBuilder.BuildBrief(await DemoReportAsync());

        Assert.Contains("subset of total spend", brief);
    }

    [Fact]
    public void The_system_prompt_forbids_inventing_or_recalculating_numbers()
    {
        // Whitespace-normalised: the instructions wrap across lines, and the test is about what
        // the prompt says, not how it is formatted.
        var prompt = System.Text.RegularExpressions.Regex.Replace(
            AiPromptBuilder.SystemPrompt, @"\s+", " ");

        Assert.Contains("Use ONLY the figures in the brief", prompt);
        Assert.Contains("never recalculate one", prompt);
        Assert.Contains("never estimate", prompt);
        Assert.Contains("Never contradict the findings", prompt);
        Assert.Contains("carry that uncertainty into your wording", prompt);
        Assert.Contains("Do not recommend anything the brief does not support", prompt);
    }

    [Fact]
    public void An_unavailable_narrative_explains_itself_without_losing_the_audit()
    {
        var narrative = AiNarrative.Unavailable("Rate limited.");

        Assert.True(narrative.Failed);
        Assert.Contains("Rate limited", narrative.Text);
    }

    [Theory]
    [InlineData(AiProvider.Anthropic, "claude-opus-5")]
    [InlineData(AiProvider.Anthropic, "claude-sonnet-5")]
    [InlineData(AiProvider.Anthropic, "claude-haiku-4-5")]
    [InlineData(AiProvider.OpenAi, "gpt-4o")]
    [InlineData(AiProvider.OpenAi, "gpt-4o-mini")]
    [InlineData(AiProvider.OpenAi, "gpt-4.1-mini")]
    public void A_cost_estimate_is_available_for_the_models_offered(AiProvider provider, string model)
    {
        var narrator = Narrator(new AiOptions { Provider = provider, ApiKey = "test", Model = model });

        Assert.True(narrator.IsConfigured);
        Assert.NotNull(narrator.TypicalCostUsd);
        Assert.True(narrator.TypicalCostUsd > 0m);
    }

    [Fact]
    public void An_unrecognised_model_gives_no_cost_estimate_rather_than_a_wrong_one()
    {
        Assert.Null(Narrator(new AiOptions { ApiKey = "test", Model = "something-else" }).TypicalCostUsd);
    }

    [Theory]
    [InlineData(AiProvider.Anthropic, "claude-opus-5")]
    [InlineData(AiProvider.OpenAi, "gpt-4o-mini")]
    public void Each_provider_has_a_default_model_so_no_one_has_to_name_one(
        AiProvider provider, string expected)
    {
        var options = new AiOptions { Provider = provider, ApiKey = "test" };

        Assert.Equal(expected, options.EffectiveModel);
        Assert.NotNull(AiPricing.TypicalReportCost(options.EffectiveModel));
    }

    [Fact]
    public void The_openai_default_is_the_cheap_model_because_cost_is_the_binding_constraint()
    {
        var openAi = AiPricing.TypicalReportCost(
            new AiOptions { Provider = AiProvider.OpenAi, ApiKey = "test" }.EffectiveModel);

        // A fraction of a penny per report, so running it on every account is affordable.
        Assert.NotNull(openAi);
        Assert.True(openAi < 0.01m, $"Expected under a cent per report, got {openAi}.");
    }

    [Fact]
    public async Task A_narrator_with_a_key_for_the_wrong_provider_fails_without_losing_the_audit()
    {
        // A key that cannot work. Whether the API rejects it or the machine has no network, the
        // result must be a value rather than an exception, so the report survives either way.
        var narrator = new OpenAiNarrator(
            new StaticAiOptionsAccessor(Microsoft.Extensions.Options.Options.Create(
                new AiOptions { Provider = AiProvider.OpenAi, ApiKey = "sk-not-a-real-key" })),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAiNarrator>.Instance);

        var narrative = await narrator.NarrateAsync(await DemoReportAsync());

        Assert.NotNull(narrative);
        Assert.True(narrative.Failed);
        Assert.Contains("audit above is unaffected", narrative.Text);
    }

    [Fact]
    public void The_prompt_forbids_attaching_a_figure_to_the_wrong_metric()
    {
        // A real failure seen in testing: the model reported impression share lost to budget as
        // 72.40% when 72.40% was the impression share and 24.80% was the loss. No number was
        // invented, but the sentence was wrong in a way a client would notice.
        var prompt = System.Text.RegularExpressions.Regex.Replace(
            AiPromptBuilder.SystemPrompt, @"\s+", " ");

        Assert.Contains("name the metric exactly as the brief names it", prompt);
        Assert.Contains("leave the number out and describe it in words instead", prompt);
    }

    private static ClaudeNarrator Narrator(AiOptions options) => new(
        new StaticAiOptionsAccessor(Microsoft.Extensions.Options.Options.Create(options)),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ClaudeNarrator>.Instance);
}
