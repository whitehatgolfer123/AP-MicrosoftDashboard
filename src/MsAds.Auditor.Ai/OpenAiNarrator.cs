using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MsAds.Auditor.Core.Ai;
using MsAds.Auditor.Core.Reports;
using OpenAI.Chat;

namespace MsAds.Auditor.Ai;

/// <summary>
/// Writes the optional commentary using OpenAI.
/// </summary>
/// <remarks>
/// The same contract as <see cref="ClaudeNarrator"/>, against a different provider: the model
/// receives only figures the deterministic engine has already calculated, is instructed to work
/// solely from them, and never sees raw data it could compute from. Whichever provider is
/// configured, the audit itself is produced identically and is complete without a commentary.
/// </remarks>
public sealed class OpenAiNarrator(
    IAiOptionsAccessor options,
    ILogger<OpenAiNarrator> logger) : IAiNarrator
{
    // Read fresh on each access: the key and model can be changed while the app is running.
    private AiOptions _options => options.Current;

    public bool IsConfigured => _options.IsConfigured;

    public string Model => _options.EffectiveModel;

    public string? DisabledReason => IsConfigured
        ? null
        : "No OpenAI API key is configured, so AI commentary is switched off. The audit itself "
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
            var client = new ChatClient(_options.EffectiveModel, _options.ApiKey);
            var brief = AiPromptBuilder.BuildBrief(report);

            var completion = await client.CompleteChatAsync(
                [
                    new SystemChatMessage(AiPromptBuilder.SystemPrompt),
                    new UserChatMessage("Here is the completed audit. Write the commentary.\n\n" + brief),
                ],
                new ChatCompletionOptions { MaxOutputTokenCount = _options.MaxTokens },
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            var value = completion.Value;

            if (!string.IsNullOrWhiteSpace(value.Refusal))
            {
                logger.LogWarning(
                    "The model declined to write a commentary for account {AccountId}",
                    report.Account.AccountId);
                return AiNarrative.Unavailable(
                    "The model declined to write a commentary. The audit above is unaffected.");
            }

            var text = string.Join(
                "\n\n",
                value.Content
                    .Select(part => part.Text?.Trim())
                    .Where(part => !string.IsNullOrEmpty(part)));

            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning(
                    "The model returned no text for account {AccountId} (finish reason {Reason})",
                    report.Account.AccountId, value.FinishReason);
                return null;
            }

            var inputTokens = value.Usage?.InputTokenCount ?? 0;
            var outputTokens = value.Usage?.OutputTokenCount ?? 0;

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
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            logger.LogWarning(ex, "AI commentary rate limited");
            return AiNarrative.Unavailable(
                "OpenAI rate limited the request, so no commentary was written this time. The "
                + "audit above is unaffected.");
        }
        catch (ClientResultException ex) when (ex.Status is 401 or 403)
        {
            logger.LogWarning(ex, "AI commentary rejected: the API key was not accepted");
            return AiNarrative.Unavailable(
                "OpenAI did not accept the API key, so no commentary was written. Check the key "
                + "on the settings page. The audit above is unaffected.");
        }
        catch (ClientResultException ex)
        {
            // Commonly an unknown model name, or no credit on the account.
            logger.LogWarning(ex, "AI commentary rejected by the API (status {Status})", ex.Status);
            return AiNarrative.Unavailable(
                $"OpenAI rejected the request, so no commentary was written. Check that the model "
                + $"name is correct and the account has credit. The audit above is unaffected.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI commentary failed");
            return AiNarrative.Unavailable(
                "The commentary could not be generated. The audit above is unaffected.");
        }
    }
}
