namespace MsAds.Auditor.Core.Demo;

/// <summary>
/// How one demo account differs from the baseline, so a portfolio scan shows the spread a real
/// agency sees rather than eight identical rows.
/// </summary>
/// <param name="CurrentSpendFactor">
/// Scales the current period's spend. Zero means the account stopped delivering.
/// </param>
/// <param name="PreviousSpendFactor">
/// Scales the comparison period's spend. Zero in both makes the account dormant.
/// </param>
/// <param name="CurrentConversionRateFactor">
/// Scales the current period's conversion rate, which is what moves CPA and therefore the
/// account's health verdict.
/// </param>
/// <param name="ReportsConversions">
/// False for an account with no conversion tracking, so the "no conversion data" path is
/// exercised in the demo rather than only in tests.
/// </param>
internal sealed record DemoAccountProfile(
    decimal CurrentSpendFactor = 1m,
    decimal PreviousSpendFactor = 1m,
    decimal CurrentConversionRateFactor = 1m,
    bool ReportsConversions = true)
{
    public static readonly DemoAccountProfile Baseline = new();

    /// <summary>The profile for each demo account, keyed by advertising account id.</summary>
    private static readonly Dictionary<long, DemoAccountProfile> Profiles = new()
    {
        // The worked example used throughout: spend up a little, conversions down a little.
        [140100001] = Baseline,

        // Clearly improving: the same spend converting better.
        [140100002] = new(CurrentSpendFactor: 1.02m, CurrentConversionRateFactor: 1.55m),

        // Stopped delivering between the two periods.
        [140100003] = new(CurrentSpendFactor: 0m),

        // Holding steady.
        [140100004] = new(CurrentSpendFactor: 1.03m, CurrentConversionRateFactor: 0.98m),

        // Declining sharply: spending more, converting much less.
        [140100005] = new(CurrentSpendFactor: 1.55m, CurrentConversionRateFactor: 0.55m),

        // Improving modestly.
        [140100006] = new(CurrentSpendFactor: 0.96m, CurrentConversionRateFactor: 1.28m),

        // Dormant: nothing in either period.
        [140100007] = new(CurrentSpendFactor: 0m, PreviousSpendFactor: 0m),

        // Spending with no conversion tracking configured.
        [140100008] = new(CurrentSpendFactor: 1.18m, ReportsConversions: false),
    };

    public static DemoAccountProfile For(long accountId)
        => Profiles.GetValueOrDefault(accountId, Baseline);
}
