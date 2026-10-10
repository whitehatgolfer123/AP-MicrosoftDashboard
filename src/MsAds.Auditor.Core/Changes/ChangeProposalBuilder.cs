using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Rules;

namespace MsAds.Auditor.Core.Changes;

/// <summary>
/// Turns a finished audit into a list of changes a person could choose to apply.
/// </summary>
/// <remarks>
/// Only findings that map onto a single, unambiguous platform operation become proposals.
/// Anything requiring judgement — reviewing a landing page, rewriting ad copy, reallocating
/// budget — stays a recommendation to read, because the application cannot make that call.
/// </remarks>
public static class ChangeProposalBuilder
{
    public static IReadOnlyList<ProposedChange> Build(AuditReport report)
    {
        var proposals = new List<ProposedChange>();

        proposals.AddRange(NegativeKeywords(report));
        proposals.AddRange(PauseKeywords(report));
        proposals.AddRange(PauseCampaigns(report));

        return [.. proposals.OrderBy(p => p.Risk).ThenByDescending(p => p.SpendAtStake ?? 0m)];
    }

    /// <summary>
    /// One exclusion per search term that cleared the full evidence bar in the waste analysis.
    /// </summary>
    private static IEnumerable<ProposedChange> NegativeKeywords(AuditReport report)
    {
        foreach (var candidate in report.SearchTerms.NegativeKeywordCandidates)
        {
            var term = candidate.Term;

            // Without a campaign to attach it to there is nowhere to add the exclusion.
            if (term.CampaignId is not { } campaignId)
            {
                continue;
            }

            yield return new ProposedChange
            {
                Id = Identifier("neg", campaignId, term.SearchTerm),
                Kind = ChangeActionKind.AddCampaignNegativeKeyword,
                Risk = ChangeRisk.Additive,
                AccountId = report.Account.AccountId,
                CustomerId = report.Account.CustomerId,
                TargetEntityId = campaignId,
                TargetDescription = $"Campaign: {term.CampaignName ?? campaignId.ToString(CultureInfo.InvariantCulture)}",
                Value = term.SearchTerm,
                // Exact match blocks precisely the query observed wasting money and nothing else.
                MatchType = "Exact",
                Summary = $"Add “{term.SearchTerm}” as an exact-match negative keyword",
                Rationale = candidate.Reason,
                Source = "Search term waste analysis",
                SpendAtStake = term.Metrics.Spend,
                Caveats =
                [
                    "Exact match is proposed deliberately: it blocks the query that was observed wasting "
                    + "money and nothing else. A phrase negative on a common word would catch more "
                    + "variations but risks blocking traffic you want, so that judgement is left to you.",
                    "This excludes the query across the whole campaign, including any ad group in it.",
                    .. candidate.Caveats,
                ],
            };
        }
    }

    /// <summary>Pausing a keyword that spent without converting.</summary>
    private static IEnumerable<ProposedChange> PauseKeywords(AuditReport report)
    {
        foreach (var finding in report.Recommendations.Findings
            .Where(f => f.RuleId == "keyword-zero-conversions" && f.EntityId is > 0))
        {
            // Thin evidence should not drive a change, only a look.
            if (finding.Confidence == FindingConfidence.Low)
            {
                continue;
            }

            yield return new ProposedChange
            {
                Id = Identifier("pausekw", finding.EntityId!.Value, finding.EntityName ?? ""),
                Kind = ChangeActionKind.PauseKeyword,
                Risk = ChangeRisk.StopsDelivery,
                AccountId = report.Account.AccountId,
                CustomerId = report.Account.CustomerId,
                TargetEntityId = finding.EntityId!.Value,
                TargetDescription = finding.EntityContext is null
                    ? $"Keyword: {finding.EntityName}"
                    : $"Keyword: {finding.EntityName} ({finding.EntityContext})",
                Summary = $"Pause the keyword {finding.EntityName}",
                Rationale = finding.Reason,
                Source = finding.RuleId,
                SpendAtStake = finding.SpendAtStake,
                Caveats =
                [
                    "Pausing stops this keyword serving entirely. If conversion tracking is the real "
                    + "problem, pausing hides the symptom rather than fixing it — check tracking first.",
                    .. finding.Caveats,
                ],
            };
        }
    }

    /// <summary>Pausing a campaign that spent without converting. The most consequential proposal.</summary>
    private static IEnumerable<ProposedChange> PauseCampaigns(AuditReport report)
    {
        foreach (var finding in report.Recommendations.Findings
            .Where(f => f.RuleId == "campaign-zero-conversions" && f.EntityId is > 0))
        {
            // Only where the evidence is strong; a campaign is a large thing to switch off.
            if (finding.Confidence != FindingConfidence.High)
            {
                continue;
            }

            yield return new ProposedChange
            {
                Id = Identifier("pausecamp", finding.EntityId!.Value, finding.EntityName ?? ""),
                Kind = ChangeActionKind.PauseCampaign,
                Risk = ChangeRisk.StopsDelivery,
                AccountId = report.Account.AccountId,
                CustomerId = report.Account.CustomerId,
                TargetEntityId = finding.EntityId!.Value,
                TargetDescription = $"Campaign: {finding.EntityName}",
                Summary = $"Pause the campaign {finding.EntityName}",
                Rationale = finding.Reason,
                Source = finding.RuleId,
                SpendAtStake = finding.SpendAtStake,
                Caveats =
                [
                    "This stops the whole campaign, every ad group and keyword in it. Consider whether "
                    + "narrowing targeting or excluding specific queries would address the problem "
                    + "without losing the traffic that does work.",
                    "A campaign with no conversions over a short window is not necessarily failing — "
                    + "check the reporting period length and the conversion tracking before pausing.",
                    .. finding.Caveats,
                ],
            };
        }
    }

    /// <summary>
    /// A stable identifier for a proposal, so a tick survives the review step and the same
    /// proposal keeps the same id between runs.
    /// </summary>
    internal static string Identifier(string prefix, long entityId, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        var digest = Convert.ToHexString(bytes)[..10].ToLowerInvariant();

        return $"{prefix}-{entityId}-{digest}";
    }
}
