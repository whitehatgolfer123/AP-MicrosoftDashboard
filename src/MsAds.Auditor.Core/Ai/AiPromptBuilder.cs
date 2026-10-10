using System.Text;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Rules;

namespace MsAds.Auditor.Core.Ai;

/// <summary>
/// Turns a finished audit into the brief handed to the model.
/// </summary>
/// <remarks>
/// <para>
/// The whole safety of this feature rests here. The model is given a closed set of already
/// calculated figures and told to work only from them, so it is writing <em>about</em> the
/// analysis rather than performing it. It never sees raw data to compute from, is never asked
/// to decide whether a threshold was crossed, and is told explicitly not to introduce numbers.
/// </para>
/// <para>
/// Built as a pure function so the instructions and the facts can be asserted in tests without
/// calling the API.
/// </para>
/// </remarks>
public static class AiPromptBuilder
{
    /// <summary>
    /// The system prompt. Deliberately restrictive: the deterministic engine has already done
    /// the analysis, and the model's job is to explain it well, not to redo it.
    /// </summary>
    public const string SystemPrompt = """
        You are writing a short commentary for a Microsoft Advertising account audit, read by an
        experienced paid-search manager at a marketing agency.

        A deterministic rules engine has already done the analysis. Every figure and every
        finding you are given has been calculated and checked. Your job is to explain what it
        means and what to do about it — not to reanalyse it.

        Rules, in order of importance:

        1. Use ONLY the figures in the brief. Never introduce a number that is not there, never
           recalculate one, and never estimate. If something is not in the brief, you do not
           know it.
        2. Keep every figure attached to the metric it belongs to. When you quote a number, name
           the metric exactly as the brief names it. Two numbers in the same sentence of the
           brief are usually two different metrics: "impression share lost to budget is 24.80%.
           Impression share is 72.40%" means the loss is 24.80% and the share is 72.40% — it
           does not mean the loss is 72.40%. If you are not certain which metric a number
           belongs to, leave the number out and describe it in words instead.
        3. Never contradict the findings. If the brief says CPA rose 14%, that is what happened.
        4. Where the brief marks something as low confidence, uncertain, or based on limited
           data, carry that uncertainty into your wording. Do not firm it up.
        5. If conversion data is unavailable, do not draw conclusions about conversion
           performance. Say what cannot be known.
        6. Prefer the specific over the general. "Competitor – Conquesting took £988 more and
           returned fewer conversions" beats "some campaigns underperformed".
        7. Do not recommend anything the brief does not support. No invented best practice.

        Write 2-4 short paragraphs in British English, plain prose, no headings, no bullet
        points, no preamble. Lead with what matters most. Be direct and concrete; an experienced
        practitioner is reading this and does not need hedging or filler.
        """;

    /// <summary>Builds the factual brief. Only calculated values appear here.</summary>
    public static string BuildBrief(AuditReport report)
    {
        var formatter = report.Formatter;
        var brief = new StringBuilder();

        brief.AppendLine("## Account");
        brief.AppendLine($"- Name: {report.Account.AccountName}");
        brief.AppendLine($"- Reporting period: {report.DateRangeLabel} ({report.Period.Current.Days} days)");
        brief.AppendLine($"- Compared with: {report.ComparisonLabel}");
        brief.AppendLine($"- Currency: {formatter.CurrencyCode}");

        if (report.IsDemo)
        {
            brief.AppendLine("- NOTE: these are synthetic demo figures, not real performance.");
        }

        brief.AppendLine();
        brief.AppendLine("## Headline metrics (already calculated — do not recalculate)");

        foreach (var card in report.Summary)
        {
            var delta = card.Delta;
            var value = formatter.Format(delta.Current, card.Key);
            var previous = delta.Previous is null ? "no comparison" : formatter.Format(delta.Previous, card.Key);
            var change = formatter.FormatChange(delta);

            brief.AppendLine(
                $"- {card.Descriptor.Name}: {value} (was {previous}, {change}) "
                + $"[{Judgement(delta)}]");
        }

        if (report.ConversionDataUnavailable)
        {
            brief.AppendLine();
            brief.AppendLine(
                "IMPORTANT: conversion data was unavailable for this account. Do not draw any "
                + "conclusion about conversion performance, CPA or ROAS.");
        }

        brief.AppendLine();
        brief.AppendLine("## Deterministic summary already shown to the reader");
        brief.AppendLine($"- {report.ExecutiveSummary.Headline}");

        foreach (var point in report.ExecutiveSummary.Points)
        {
            brief.AppendLine($"- {point}");
        }

        AppendFindings(brief, report);
        AppendSearchTerms(brief, report, formatter);
        AppendCaveats(brief, report);

        return brief.ToString();
    }

    private static void AppendFindings(StringBuilder brief, AuditReport report)
    {
        // The most material findings only. The full list is on screen; this is for context.
        var findings = report.Recommendations.Findings.Take(12).ToList();

        if (findings.Count == 0)
        {
            return;
        }

        brief.AppendLine();
        brief.AppendLine("## Findings from the rules engine");

        foreach (var finding in findings)
        {
            brief.AppendLine(
                $"- [{finding.Severity}, {finding.Confidence} confidence] {finding.Title}");
            brief.AppendLine($"  Why: {finding.Reason}");
            brief.AppendLine($"  Suggested: {finding.SuggestedAction}");

            if (finding.Caveats.Count > 0)
            {
                brief.AppendLine($"  Caveat: {finding.Caveats[0]}");
            }
        }
    }

    private static void AppendSearchTerms(StringBuilder brief, AuditReport report, MetricFormatter formatter)
    {
        var analysis = report.SearchTerms;

        if (analysis.DataUnavailable)
        {
            brief.AppendLine();
            brief.AppendLine(
                "## Search terms\n- Unavailable for this period. This is not evidence that no "
                + "wasted spend exists; say so if you mention search terms at all.");
            return;
        }

        if (analysis.TermCount == 0)
        {
            return;
        }

        brief.AppendLine();
        brief.AppendLine("## Search terms");
        brief.AppendLine(
            $"- {analysis.TermCount} queries reported, covering {formatter.FormatCurrency(analysis.SpendAnalysed)} "
            + $"of spend (a subset of total spend — Microsoft does not publish every query).");
        brief.AppendLine(
            $"- {formatter.FormatCurrency(analysis.UnproductiveSpend)} went to queries with no recorded conversions.");
        brief.AppendLine(
            $"- {analysis.SuggestedNegativeKeywords.Count} term(s) meet the bar to propose as negative keywords.");

        foreach (var candidate in analysis.NegativeKeywordCandidates.Take(6))
        {
            brief.AppendLine(
                $"  - \"{candidate.Term.SearchTerm}\": {formatter.FormatCurrency(candidate.Term.Metrics.Spend)}, "
                + $"{candidate.Term.Metrics.Clicks} clicks, no conversions");
        }
    }

    private static void AppendCaveats(StringBuilder brief, AuditReport report)
    {
        var caveats = report.ExecutiveSummary.Caveats;

        if (caveats.Count == 0)
        {
            return;
        }

        brief.AppendLine();
        brief.AppendLine("## Data limitations the reader has already been shown");

        foreach (var caveat in caveats)
        {
            brief.AppendLine($"- {caveat}");
        }
    }

    /// <summary>
    /// States whether a movement is favourable, so the model does not have to infer polarity
    /// and cannot get it backwards — a rise in spend is not good news, a rise in CPA is bad.
    /// </summary>
    private static string Judgement(MetricDelta delta) => delta.Verdict switch
    {
        Verdict.Positive => "favourable",
        Verdict.Negative => "adverse",
        Verdict.Neutral => "neither good nor bad on its own",
        _ => "not comparable",
    };
}
