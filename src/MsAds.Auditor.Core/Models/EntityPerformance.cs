using MsAds.Auditor.Core.Metrics;

namespace MsAds.Auditor.Core.Models;

/// <summary>One day's totals, used for the trend charts.</summary>
public sealed record DailyMetrics(DateOnly Date, MetricSet Metrics);

/// <summary>
/// A campaign's performance over one period, including the impression-share columns where the
/// account makes them available.
/// </summary>
public sealed record CampaignPerformance
{
    public required long CampaignId { get; init; }

    public required string CampaignName { get; init; }

    /// <summary>Campaign status as reported, e.g. Active, Paused.</summary>
    public string? Status { get; init; }

    public string? CampaignType { get; init; }

    public required MetricSet Metrics { get; init; }

    /// <summary>Impression share as a percentage, where available.</summary>
    public decimal? ImpressionShare { get; init; }

    /// <summary>Impression share lost to budget, as a percentage, where available.</summary>
    public decimal? ImpressionShareLostToBudget { get; init; }

    /// <summary>Impression share lost to rank, as a percentage, where available.</summary>
    public decimal? ImpressionShareLostToRank { get; init; }

    public bool HasImpressionShareData => ImpressionShare.HasValue;
}

/// <summary>
/// A search query that triggered ads, as reported by the search query performance report.
/// </summary>
public sealed record SearchTermPerformance
{
    public required string SearchTerm { get; init; }

    public long? CampaignId { get; init; }

    public string? CampaignName { get; init; }

    public long? AdGroupId { get; init; }

    public string? AdGroupName { get; init; }

    /// <summary>The keyword that the query matched, where reported.</summary>
    public string? Keyword { get; init; }

    /// <summary>The match type of the bid that matched, where reported.</summary>
    public string? BidMatchType { get; init; }

    /// <summary>How the query actually matched the keyword, where reported.</summary>
    public string? DeliveredMatchType { get; init; }

    public required MetricSet Metrics { get; init; }
}

/// <summary>A keyword's performance over one period.</summary>
public sealed record KeywordPerformance
{
    public required long KeywordId { get; init; }

    public required string Keyword { get; init; }

    public string? MatchType { get; init; }

    public long? CampaignId { get; init; }

    public string? CampaignName { get; init; }

    public long? AdGroupId { get; init; }

    public string? AdGroupName { get; init; }

    public string? Status { get; init; }

    public required MetricSet Metrics { get; init; }

    /// <summary>Quality score, where reported.</summary>
    public decimal? QualityScore { get; init; }

    /// <summary>The current bid, where reported.</summary>
    public decimal? CurrentBid { get; init; }
}

/// <summary>
/// A recorded change to the account, from the search campaign change history report. Only what
/// the API actually reports is represented — nothing is inferred.
/// </summary>
public sealed record ChangeRecord
{
    /// <summary>When the change was recorded, in the account's reporting time zone.</summary>
    public required DateTimeOffset ChangedAt { get; init; }

    /// <summary>The user who made the change, where reported.</summary>
    public string? ChangedBy { get; init; }

    /// <summary>The kind of entity changed, e.g. Campaign, AdGroup, Keyword.</summary>
    public required string EntityType { get; init; }

    public string? EntityName { get; init; }

    public long? CampaignId { get; init; }

    public string? CampaignName { get; init; }

    public string? AdGroupName { get; init; }

    /// <summary>The attribute that changed, e.g. Budget, Bid, Status.</summary>
    public string? AttributeChanged { get; init; }

    /// <summary>What kind of change it was, e.g. Added, Changed, Deleted.</summary>
    public string? HowChanged { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public DateOnly ChangedOn => DateOnly.FromDateTime(ChangedAt.Date);

    public string Describe()
    {
        var attribute = AttributeChanged ?? "Setting";

        return (OldValue, NewValue) switch
        {
            (not null, not null) => $"{attribute}: {OldValue} → {NewValue}",
            (null, not null) => $"{attribute} set to {NewValue}",
            (not null, null) => $"{attribute} cleared (was {OldValue})",
            _ => $"{attribute} {HowChanged?.ToLowerInvariant() ?? "changed"}",
        };
    }
}

/// <summary>
/// A keyword blocked by one of the account's own negative keywords, from the negative keyword
/// conflict report.
/// </summary>
public sealed record NegativeKeywordConflict
{
    public long? CampaignId { get; init; }

    public string? CampaignName { get; init; }

    public string? AdGroupName { get; init; }

    public required string Keyword { get; init; }

    public required string NegativeKeyword { get; init; }

    /// <summary>Whether the conflict arises at campaign or ad group level.</summary>
    public string? ConflictLevel { get; init; }

    public string? NegativeKeywordListName { get; init; }
}
