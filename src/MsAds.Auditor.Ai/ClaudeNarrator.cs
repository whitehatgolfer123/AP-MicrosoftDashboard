using System.Diagnostics;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using MsAds.Auditor.Core.Ai;
using MsAds.Auditor.Core.Reports;

namespace MsAds.Auditor.Ai;

/// <summary>
/// Writes the optional commentary using Claude.
/// </summary>
/// <remarks>
/// <para>
/// The model is given only figures the deterministic engine has already calculated, and is
/// instructed to work solely from them. It never decides whether a threshold was crossed and
/// never sees raw data to compute from, so its output cannot change the analysis — only
/// describe it.
/// </para>
/// <para>
/// Every failure is swallowed into a null result. A commentary is a bonus; losing it must never
/// cost the user the audit they actually asked for.
/// </para>
/// </remarks>
public sealed class ClaudeNarrator(
    IAiOptionsAccessor options,
    ILogger<ClaudeNarrator> logger) : IAiNarrator
{
    // Read fresh on each access: the key and model can be changed while the app is running.
    private AiOptions _options => options.Current;

    public bool IsConfigured => _options.IsConfigured;

    public string Model => _options.EffectiveModel;

    public string? DisabledReason => IsConfigured
        ? null
        : "No Anthropic API key is configured, so AI commentary is switched off. The audit itself "
          + "is unaffected — every figure and finding is produced without it.";

    /// <summary>A rough per-report cost for the configured model, for display.</summary>
    public decimal? TypicalCostUsd => AiPricing.TypicalReportCost(_options.EffectiveModel);

    public async Task<AiNarrative?> NarrateAsync(
        AuditReport report, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = new AnthropicClient { ApiKey = _options.ApiKey };
            var brief = AiPromptBuilder.BuildBrief(report);

            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = _options.EffectiveModel,
                MaxTokens = _options.MaxTokens,
                System = AiPromptBuilder.SystemPrompt,
                // A short piece of writing from a closed set of supplied facts is not a hard
                // reasoning problem, and the person running this pays per call, so it runs at
                // low effort rather than the default.
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                Thinking = new ThinkingConfigAdaptive(),
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content =
                            "Here is the completed audit. Write the commentary.\n\n" + brief,
                    },
                ],
            }).WaitAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var text = string.Join(
                "\n\n",
                response.Content
                    .Select(block => block.Value)
                    .OfType<TextBlock>()
                    .Select(block => block.Text.Trim())
                    .Where(value => value.Length > 0));

            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning("The model returned no text for account {AccountId}", report.Account.AccountId);
                return null;
            }

            var inputTokens = (int)response.Usage.InputTokens;
            var outputTokens = (int)response.Usage.OutputTokens;

            logger.LogInformation(
                "AI commentary for account {AccountId}: {Input} in, {Output} out, {Seconds:N1}s",
                report.Account.AccountId, inputTokens, outputTokens, stopwatch.Elapsed.TotalSeconds);

            return new AiNarrative
            {
                Text = text,
                Model = _options.EffectiveModel,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                EstimatedCostUsd = AiPricing.Estimate(_options.EffectiveModel, inputTokens, outputTokens),
                Duration = stopwatch.Elapsed,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AnthropicRateLimitException ex)
        {
            logger.LogWarning(ex, "AI commentary rate limited");
            return AiNarrative.Unavailable(
                "Anthropic rate limited the request, so no commentary was written this time. The "
                + "audit above is unaffected.");
        }
        catch (AnthropicApiException ex)
        {
            // Usually a bad or expired key, or no credit on the account.
            logger.LogWarning(ex, "AI commentary rejected by the API");
            return AiNarrative.Unavailable(
                "Anthropic rejected the request, so no commentary was written. Check the API key "
                + "and that the account has credit. The audit above is unaffected.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI commentary failed");
            return AiNarrative.Unavailable(
                "The commentary could not be generated. The audit above is unaffected.");
        }
    }
}
