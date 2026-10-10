namespace MsAds.Auditor.Core.Demo;

/// <summary>
/// The per-day rates that define one demo campaign in one period. Expressing the demo as rates,
/// with separate current and previous values, means each planted scenario holds whichever
/// reporting period length the user selects.
/// </summary>
internal sealed record DemoCampaignRates(
    decimal SpendPerDay,
    decimal Cpc,
    decimal CtrPercent,
    decimal ConversionRatePercent,
    decimal ValuePerConversion);

/// <summary>
/// One demo campaign, with the scenario it is designed to demonstrate. The scenarios between
/// them exercise every rule in the engine, so the demo dashboard shows a realistic mix of
/// problems and successes rather than uniformly good or bad numbers.
/// </summary>
internal sealed record DemoCampaignSpec(
    long Id,
    string Name,
    string Type,
    string Status,
    string Scenario,
    DemoCampaignRates Current,
    DemoCampaignRates Previous,
    decimal? ImpressionShare = null,
    decimal? ImpressionShareLostToBudget = null,
    decimal? ImpressionShareLostToRank = null);

/// <summary>
/// The demo account's campaigns, keywords, search terms and change records. Modelled on a
/// domestic heating installer so the search terms read plausibly.
/// </summary>
internal static class DemoScenario
{
    /// <summary>Weekday weighting, so the daily charts show a believable weekly rhythm.</summary>
    public static decimal DayWeight(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 1.12m,
        DayOfWeek.Tuesday => 1.10m,
        DayOfWeek.Wednesday => 1.05m,
        DayOfWeek.Thursday => 1.00m,
        DayOfWeek.Friday => 0.92m,
        DayOfWeek.Saturday => 0.68m,
        _ => 0.63m,
    };

    public static IReadOnlyList<DemoCampaignSpec> Campaigns { get; } =
    [
        // Efficient brand campaign, but held back by budget: exercises the scaling and
        // impression-share-lost-to-budget rules.
        new(1001, "Brand – Exact", "Search", "Active",
            "Strong performer constrained by budget",
            new(42.00m, 0.58m, 11.40m, 9.60m, 92.00m),
            new(39.50m, 0.56m, 11.80m, 9.90m, 90.00m),
            ImpressionShare: 72.4m, ImpressionShareLostToBudget: 24.8m, ImpressionShareLostToRank: 2.8m),

        // The account's workhorse: acceptable but unremarkable.
        new(1002, "Generic – Core services", "Search", "Active",
            "Mid-performing campaign near the account average",
            new(128.00m, 1.92m, 4.10m, 2.55m, 74.00m),
            new(121.00m, 1.86m, 4.25m, 2.80m, 74.00m),
            ImpressionShare: 38.6m, ImpressionShareLostToBudget: 9.4m, ImpressionShareLostToRank: 52.0m),

        // Spend rising while conversions fall: the headline pattern the brief asks for.
        new(1003, "Competitor – Conquesting", "Search", "Active",
            "Rising spend with falling conversions and a worsening CPA",
            new(96.00m, 2.68m, 2.05m, 0.62m, 68.00m),
            new(57.00m, 2.21m, 2.70m, 1.55m, 70.00m),
            ImpressionShare: 21.3m, ImpressionShareLostToBudget: 4.1m, ImpressionShareLostToRank: 74.6m),

        // Spends steadily and converts nothing: the clearest waste candidate.
        new(1004, "Generic – Broad discovery", "Search", "Active",
            "Meaningful spend with no recorded conversions",
            new(71.00m, 1.28m, 2.85m, 0.00m, 0.00m),
            new(64.00m, 1.22m, 3.00m, 0.00m, 0.00m),
            ImpressionShare: 17.9m, ImpressionShareLostToBudget: 6.2m, ImpressionShareLostToRank: 75.9m),

        // Shopping campaign with a healthy return: exercises the ROAS path.
        new(1005, "Shopping – Boilers & parts", "Shopping", "Active",
            "Healthy return on ad spend",
            new(154.00m, 0.44m, 0.88m, 1.95m, 128.00m),
            new(142.00m, 0.43m, 0.86m, 1.88m, 124.00m),
            ImpressionShare: 44.2m, ImpressionShareLostToBudget: 18.7m, ImpressionShareLostToRank: 37.1m),

        // Delivered last period and nothing this period: exercises the delivery rule.
        new(1006, "Remarketing – Site visitors", "Audience", "Paused",
            "Stopped delivering between periods",
            new(0m, 0m, 0m, 0m, 0m),
            new(34.00m, 0.78m, 0.62m, 3.40m, 96.00m),
            ImpressionShare: null, ImpressionShareLostToBudget: null, ImpressionShareLostToRank: null),

        // Click-through rate falling sharply: exercises the relevance rule.
        new(1007, "Dynamic Search Ads", "DynamicSearchAds", "Active",
            "Sharply declining click-through rate",
            new(56.00m, 1.46m, 3.05m, 2.15m, 66.00m),
            new(52.00m, 1.34m, 4.60m, 2.40m, 66.00m),
            ImpressionShare: 29.8m, ImpressionShareLostToBudget: 7.7m, ImpressionShareLostToRank: 62.5m),
    ];

    /// <summary>
    /// Demo search terms: the query, the campaign it belongs to, the keyword that matched it, and
    /// the per-period rates. Chosen so the waste analyser has clear waste, clear winners, and
    /// genuinely ambiguous low-volume terms to be cautious about.
    /// </summary>
    public static IReadOnlyList<DemoSearchTermSpec> SearchTerms { get; } =
    [
        // Clear waste: job seekers and researchers, not customers.
        new("boiler engineer jobs", 1004, "Broad discovery", "boiler engineer", "Broad", 0.00m, 18, 1.42m),
        new("boiler engineer salary uk", 1004, "Broad discovery", "boiler engineer", "Broad", 0.00m, 14, 1.31m),
        new("how to fix a boiler yourself", 1004, "Broad discovery", "boiler repair", "Broad", 0.00m, 22, 1.18m),
        new("free boiler grant scheme", 1004, "Broad discovery", "new boiler", "Broad", 0.00m, 31, 1.26m),
        new("boiler repair training course", 1004, "Broad discovery", "boiler repair", "Broad", 0.00m, 11, 1.35m),
        new("diy radiator bleeding guide", 1004, "Broad discovery", "radiator installation", "Broad", 0.00m, 9, 1.08m),

        // Expensive competitor traffic that does not convert.
        new("britishgas boiler cover price", 1003, "Conquesting", "british gas alternative", "Phrase", 0.00m, 16, 3.12m),
        new("hometree vs british gas", 1003, "Conquesting", "british gas alternative", "Phrase", 0.00m, 12, 2.94m),
        new("cheapest boiler cover uk", 1003, "Conquesting", "cheap boiler cover", "Phrase", 0.00m, 24, 2.68m),

        // Converting but inefficiently: a landing-page or tracking question.
        new("emergency boiler repair near me", 1002, "Core services", "emergency boiler repair", "Exact", 0.60m, 42, 2.21m),
        new("boiler service cost", 1002, "Core services", "boiler service", "Phrase", 0.90m, 38, 1.84m),

        // Strong performers.
        new("new boiler installation quote", 1002, "Core services", "new boiler installation", "Exact", 4.80m, 56, 1.96m),
        new("combi boiler replacement cost", 1002, "Core services", "combi boiler replacement", "Phrase", 3.60m, 44, 1.88m),
        new("worcester bosch boiler install", 1001, "Brand terms", "worcester bosch installer", "Exact", 5.20m, 48, 0.62m),
        new("gasfit heating installation", 1001, "Brand terms", "gasfit heating", "Exact", 6.40m, 62, 0.54m),
        new("gasfit boiler service booking", 1001, "Brand terms", "gasfit boiler service", "Exact", 4.10m, 39, 0.56m),

        // Low volume: deliberately below the thresholds, to show the analyser declining to judge.
        new("boiler installation sunday", 1002, "Core services", "boiler installation", "Phrase", 0.00m, 3, 1.72m),
        new("vaillant boiler quote", 1002, "Core services", "vaillant boiler", "Phrase", 0.00m, 4, 1.65m),
        new("underfloor heating retrofit", 1002, "Core services", "underfloor heating", "Broad", 0.00m, 2, 2.05m),
        new("boiler finance options", 1002, "Core services", "boiler finance", "Phrase", 1.00m, 6, 1.58m),
    ];

    public static IReadOnlyList<DemoKeywordSpec> Keywords { get; } =
    [
        new(2001, "gasfit heating", "Exact", 1001, "Brand – Exact", "Brand terms", 0.56m, 12.40m, 10.20m, 9.0m, 1.20m),
        new(2002, "gasfit boiler service", "Exact", 1001, "Brand – Exact", "Brand terms", 0.58m, 11.10m, 8.80m, 8.0m, 1.10m),
        new(2003, "new boiler installation", "Exact", 1002, "Generic – Core services", "Installation", 2.05m, 5.20m, 3.60m, 7.0m, 2.60m),
        new(2004, "combi boiler replacement", "Phrase", 1002, "Generic – Core services", "Installation", 1.96m, 4.60m, 3.10m, 6.0m, 2.40m),
        new(2005, "emergency boiler repair", "Exact", 1002, "Generic – Core services", "Repairs", 2.24m, 6.10m, 1.40m, 6.0m, 2.70m),
        new(2006, "boiler service", "Phrase", 1002, "Generic – Core services", "Servicing", 1.78m, 4.90m, 1.90m, 6.0m, 2.10m),
        new(2007, "boiler engineer", "Broad", 1004, "Generic – Broad discovery", "Discovery", 1.31m, 2.90m, 0.00m, 4.0m, 1.60m),
        new(2008, "boiler repair", "Broad", 1004, "Generic – Broad discovery", "Discovery", 1.26m, 2.70m, 0.00m, 4.0m, 1.50m),
        new(2009, "radiator installation", "Broad", 1004, "Generic – Broad discovery", "Discovery", 1.12m, 2.40m, 0.00m, 3.0m, 1.35m),
        new(2010, "british gas alternative", "Phrase", 1003, "Competitor – Conquesting", "Competitors", 3.08m, 2.10m, 0.40m, 3.0m, 3.60m),
        new(2011, "cheap boiler cover", "Phrase", 1003, "Competitor – Conquesting", "Competitors", 2.64m, 2.20m, 0.70m, 4.0m, 3.10m),
        new(2012, "worcester bosch installer", "Exact", 1001, "Brand – Exact", "Brand terms", 0.62m, 9.80m, 7.90m, 8.0m, 1.00m),
        new(2013, "underfloor heating", "Broad", 1002, "Generic – Core services", "Installation", 2.12m, 3.10m, 1.10m, 5.0m, 2.80m),
        new(2014, "boiler finance", "Phrase", 1002, "Generic – Core services", "Installation", 1.62m, 3.80m, 2.20m, 5.0m, 2.00m),
    ];
}

/// <summary>A demo search term and its per-period rates.</summary>
/// <param name="ConversionRatePercent">Deliberately zero for the terms planted as waste.</param>
internal sealed record DemoSearchTermSpec(
    string Term,
    long CampaignId,
    string AdGroup,
    string Keyword,
    string MatchType,
    decimal ConversionRatePercent,
    int ClicksPerPeriodAt30Days,
    decimal Cpc);

/// <summary>A demo keyword and its per-period rates.</summary>
internal sealed record DemoKeywordSpec(
    long Id,
    string Keyword,
    string MatchType,
    long CampaignId,
    string CampaignName,
    string AdGroup,
    decimal Cpc,
    decimal CtrPercent,
    decimal ConversionRatePercent,
    decimal QualityScore,
    decimal ClicksPerDay);
