namespace MsAds.Auditor.Ai;

/// <summary>
/// Per-token prices, so the cost of a call can be shown to whoever is paying for it.
/// </summary>
/// <remarks>
/// Published list prices in US dollars per million tokens, correct at the time of writing.
/// Prices change, so an unknown model yields no estimate rather than a wrong one — the token
/// counts are always shown and are exact.
/// </remarks>
public static class AiPricing
{
    private static readonly Dictionary<string, (decimal Input, decimal Output)> PerMillionTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Anthropic
            ["claude-opus-5"] = (5.00m, 25.00m),
            ["claude-opus-4-8"] = (5.00m, 25.00m),
            ["claude-sonnet-5"] = (2.00m, 10.00m),
            ["claude-haiku-4-5"] = (1.00m, 5.00m),

            // OpenAI
            ["gpt-4o"] = (2.50m, 10.00m),
            ["gpt-4o-mini"] = (0.15m, 0.60m),
            ["gpt-4.1"] = (2.00m, 8.00m),
            ["gpt-4.1-mini"] = (0.40m, 1.60m),
        };

    /// <summary>The dollar cost of one call, or null when the model's price is not known here.</summary>
    public static decimal? Estimate(string model, int inputTokens, int outputTokens)
    {
        if (!PerMillionTokens.TryGetValue(model, out var price))
        {
            return null;
        }

        return (inputTokens * price.Input / 1_000_000m) + (outputTokens * price.Output / 1_000_000m);
    }

    /// <summary>A rough per-report cost, for showing before anyone commits to spending.</summary>
    public static decimal? TypicalReportCost(string model)
        // A brief runs roughly 2,500 input tokens and the commentary roughly 600 output.
        => Estimate(model, 2_500, 600);
}
