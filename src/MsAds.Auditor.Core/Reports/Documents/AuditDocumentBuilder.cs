using MsAds.Auditor.Core.Analysis;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Models;
using MsAds.Auditor.Core.Rules;

namespace MsAds.Auditor.Core.Reports.Documents;

/// <summary>
/// Turns a completed audit into a renderable document.
/// </summary>
/// <remarks>
/// <para>
/// Every figure here comes from <see cref="AuditReport"/> and is formatted by the report's own
/// <see cref="MetricFormatter"/>, so a PDF and the dashboard cannot show different numbers for
/// the same account. Nothing is recalculated.
/// </para>
/// <para>
/// Two documents are offered rather than one. The executive summary is the version that gets
/// forwarded — a few pages, the verdict and the material findings. The account audit is the
/// working document, with the campaign, keyword and search-term detail behind it.
/// </para>
/// </remarks>
public static class AuditDocumentBuilder
{
    /// <summary>How many rows of each detail table a document carries.</summary>
    private const int DetailRowLimit = 25;

    /// <summary>Available artefacts, used to build the download links.</summary>
    public static IReadOnlyList<(string Key, string Label, string Description)> Available { get; } =
    [
        ("executive", "Executive summary",
            "The verdict, the headline figures and the material findings. A few pages, suitable for forwarding."),
        ("account", "Full account audit",
            "Everything in the executive summary plus the campaign, keyword and search-term detail behind it."),
    ];

    public static ReportDocument Build(AuditReport report, string key, BrandOptions brand) => key switch
    {
        "executive" => Executive(report, brand),
        "account" => Account(report, brand),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown artefact."),
    };

    public static string FileName(AuditReport report, string key)
        => $"{report.Account.AccountId}-{key}-audit"
           + $"-{report.Period.Current.Start:yyyyMMdd}-{report.Period.Current.End:yyyyMMdd}"
           + (report.IsDemo ? "-DEMO" : "");

    // --- documents ---------------------------------------------------------------------------

    public static ReportDocument Executive(AuditReport report, BrandOptions brand)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(brand);

        var blocks = new List<DocumentBlock>();

        blocks.AddRange(SummarySection(report));
        blocks.AddRange(HeadlineMetrics(report));
        blocks.AddRange(FindingsSection(report, materialOnly: true));
        blocks.AddRange(NoticesSection(report));

        return new ReportDocument
        {
            Title = "Executive summary",
            Subtitle = $"{report.Account.AccountName} — {report.DateRangeLabel}",
            FileStem = FileName(report, "executive"),
            Provenance = Provenance(report, brand),
            Blocks = blocks,
        };
    }

    public static ReportDocument Account(AuditReport report, BrandOptions brand)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(brand);

        var blocks = new List<DocumentBlock>();

        blocks.AddRange(SummarySection(report));
        blocks.AddRange(HeadlineMetrics(report));
        blocks.AddRange(FindingsSection(report, materialOnly: false));

        blocks.Add(new PageBreakBlock());
        blocks.AddRange(CampaignSection(report));
        blocks.AddRange(KeywordSection(report));
        blocks.AddRange(SearchTermSection(report));
        blocks.AddRange(ChangeHistorySection(report));
        blocks.AddRange(NoticesSection(report));

        return new ReportDocument
        {
            Title = "Account audit",
            Subtitle = $"{report.Account.AccountName} — {report.DateRangeLabel}",
            FileStem = FileName(report, "account"),
            Provenance = Provenance(report, brand),
            Blocks = blocks,
        };
    }

    // --- sections ----------------------------------------------------------------------------

    private static IEnumerable<DocumentBlock> SummarySection(AuditReport report)
    {
        yield return new HeadingBlock("Summary");
        yield return new ParagraphBlock(report.ExecutiveSummary.Headline);

        if (report.ExecutiveSummary.Points.Count > 0)
        {
            yield return new BulletsBlock(report.ExecutiveSummary.Points);
        }

        foreach (var caveat in report.ExecutiveSummary.Caveats)
        {
            yield return new NoticeBlock(caveat, DocumentTone.Caution);
        }
    }

    private static IEnumerable<DocumentBlock> HeadlineMetrics(AuditReport report)
    {
        if (report.Summary.Count == 0)
        {
            yield break;
        }

        yield return new HeadingBlock("Performance");

        yield return new MetricCardsBlock(
        [
            .. report.Summary.Select(comparison => new MetricCard(
                comparison.Descriptor.ShortName,
                report.Formatter.Format(comparison.Delta.Current, comparison.Key),
                report.Formatter.FormatChange(comparison.Delta),
                ToneOf(comparison.Delta.Verdict))),
        ]);

        if (report.Period.Previous is null)
        {
            yield return new NoticeBlock(
                "No comparison period was requested, so the movement columns are empty.",
                DocumentTone.Neutral);
        }
    }

    private static IEnumerable<DocumentBlock> FindingsSection(AuditReport report, bool materialOnly)
    {
        var findings = report.Recommendations.Findings;

        if (materialOnly)
        {
            // The forwarded version carries what is costing money or needs investigating.
            // Positive and informational findings stay in the full audit.
            findings =
            [
                .. findings.Where(f =>
                    f.Severity is FindingSeverity.Critical or FindingSeverity.Warning),
            ];
        }

        yield return new HeadingBlock(materialOnly ? "What needs attention" : "Findings");

        if (findings.Count == 0)
        {
            yield return new ParagraphBlock(
                materialOnly
                    ? "No critical or warning findings were raised for this period."
                    : "No findings were raised for this period.");
        }

        // Most financially material first, which is the order the interface uses.
        foreach (var finding in findings
            .OrderBy(f => f.Severity == FindingSeverity.Critical ? 0 : f.Severity == FindingSeverity.Warning ? 1 : 2)
            .ThenByDescending(f => f.SpendAtStake ?? 0m))
        {
            yield return new FindingBlock(
                finding.RuleId,
                finding.Title,
                finding.Severity.ToString(),
                finding.Confidence.ToString(),
                finding.Category,
                finding.EntityName is null ? finding.EntityType : $"{finding.EntityType}: {finding.EntityName}",
                finding.Reason,
                finding.SuggestedAction,
                [
                    .. finding.Evidence.Select(e => new KeyValuePair<string, string>(
                        e.Label,
                        e.Change is null ? e.Value : $"{e.Value} ({e.Change})")),
                ],
                finding.Caveats,
                ToneOf(finding.Severity));
        }

        if (report.Recommendations.RuleFailures.Count > 0)
        {
            // A rule that could not run is not a clean bill of health, and a PDF must not imply
            // that it was.
            yield return new NoticeBlock(
                $"{report.Recommendations.RuleFailures.Count} rule(s) could not be evaluated, so "
                + "this list may be incomplete: "
                + string.Join(", ", report.Recommendations.RuleFailures),
                DocumentTone.Negative);
        }
    }

    private static IEnumerable<DocumentBlock> CampaignSection(AuditReport report)
    {
        yield return new HeadingBlock("Campaigns");

        yield return EntityTable(
            report,
            report.Campaigns,
            "No campaign data was returned for this period.");
    }

    private static IEnumerable<DocumentBlock> KeywordSection(AuditReport report)
    {
        if (report.Keywords.Count == 0)
        {
            yield break;
        }

        yield return new HeadingBlock("Keywords");
        yield return EntityTable(report, report.Keywords, "No keyword data was returned.");
    }

    private static TableBlock EntityTable(
        AuditReport report, IReadOnlyList<ComparedEntity> entities, string emptyMessage)
    {
        var formatter = report.Formatter;

        var rows = entities
            .OrderByDescending(e => e.Current.Spend)
            .Take(DetailRowLimit)
            .Select(entity => (IReadOnlyList<string>)
            [
                entity.Name,
                formatter.Format(entity.Current.Spend, MetricKey.Spend),
                formatter.Format(entity.Current.Clicks, MetricKey.Clicks),
                formatter.Format(entity.Current.ClickThroughRate, MetricKey.ClickThroughRate),
                formatter.Format(entity.Current.Conversions, MetricKey.Conversions),
                formatter.Format(entity.Current.CostPerAcquisition, MetricKey.CostPerAcquisition),
                formatter.FormatChange(entity.Delta(MetricKey.Spend)),
            ])
            .ToList();

        return new TableBlock(
            ["Name", "Spend", "Clicks", "CTR", "Conv.", "CPA", "Spend change"],
            rows,
            [
                ColumnAlignment.Left,
                ColumnAlignment.Right,
                ColumnAlignment.Right,
                ColumnAlignment.Right,
                ColumnAlignment.Right,
                ColumnAlignment.Right,
                ColumnAlignment.Right,
            ],
            entities.Count == 0 ? emptyMessage : TruncationNote(entities.Count));
    }

    private static IEnumerable<DocumentBlock> SearchTermSection(AuditReport report)
    {
        var analysis = report.SearchTerms;

        yield return new HeadingBlock("Search terms");

        if (analysis.DataUnavailable)
        {
            // "Could not retrieve" and "retrieved, nothing to flag" are different conclusions
            // and must not read the same on the page.
            yield return new NoticeBlock(
                "Search term data could not be retrieved for this account and period, so no "
                + "waste analysis was possible.",
                DocumentTone.Caution);
            yield break;
        }

        if (analysis.TermCount == 0)
        {
            yield return new ParagraphBlock("No search terms were returned for this period.");
            yield break;
        }

        yield return new ParagraphBlock(
            $"{analysis.TermCount} search term(s) assessed, of which {analysis.FlaggedCount} were "
            + $"flagged. {report.Formatter.Format(analysis.UnproductiveSpend, MetricKey.Spend)} of "
            + "spend on flagged terms recorded no conversions.");

        var flagged = analysis.Flagged
            .OrderByDescending(a => a.Term.Metrics.Spend)
            .Take(DetailRowLimit)
            .ToList();

        yield return new TableBlock(
            ["Search term", "Spend", "Clicks", "Conv.", "Why flagged", "Suggested action"],
            [
                .. flagged.Select(a => (IReadOnlyList<string>)
                [
                    a.Term.SearchTerm,
                    report.Formatter.Format(a.Term.Metrics.Spend, MetricKey.Spend),
                    report.Formatter.Format(a.Term.Metrics.Clicks, MetricKey.Clicks),
                    report.Formatter.Format(a.Term.Metrics.Conversions, MetricKey.Conversions),
                    a.FlagSummary,
                    a.RecommendedAction.Label(),
                ]),
            ],
            [
                ColumnAlignment.Left,
                ColumnAlignment.Right,
                ColumnAlignment.Right,
                ColumnAlignment.Right,
                ColumnAlignment.Left,
                ColumnAlignment.Left,
            ],
            flagged.Count == 0
                ? "No search terms were flagged."
                : TruncationNote(analysis.FlaggedCount));

        var candidates = analysis.SuggestedNegativeKeywords;

        if (candidates.Count > 0)
        {
            yield return new HeadingBlock("Proposed negative keywords", 2);
            yield return new ParagraphBlock(
                "Candidates only. Nothing in this document has been applied to the account, and "
                + "exclusions are never applied automatically.");
            yield return new BulletsBlock([.. candidates.Take(DetailRowLimit)]);
        }
    }

    private static IEnumerable<DocumentBlock> ChangeHistorySection(AuditReport report)
    {
        if (report.ChangeHistoryUnavailable)
        {
            yield return new HeadingBlock("Change history");
            yield return new NoticeBlock(
                "Change history was not available for this account and period.",
                DocumentTone.Caution);
            yield break;
        }

        if (report.Changes.Count == 0)
        {
            yield break;
        }

        yield return new HeadingBlock("Change history");

        yield return new TableBlock(
            ["Date", "Entity", "What changed", "By"],
            [
                .. report.Changes
                    .OrderByDescending(c => c.ChangedAt)
                    .Take(DetailRowLimit)
                    .Select(change => (IReadOnlyList<string>)
                    [
                        report.Formatter.FormatDate(change.ChangedOn),
                        change.EntityName is null
                            ? change.EntityType
                            : $"{change.EntityType}: {change.EntityName}",

                        // Describe() is shared with the interface, so a change cannot be
                        // summarised one way on screen and another on the page.
                        change.Describe(),
                        change.ChangedBy ?? "—",
                    ]),
            ],
            [
                ColumnAlignment.Left,
                ColumnAlignment.Left,
                ColumnAlignment.Left,
                ColumnAlignment.Left,
            ],
            TruncationNote(report.Changes.Count));
    }

    private static IEnumerable<DocumentBlock> NoticesSection(AuditReport report)
    {
        if (report.Notices.Count == 0)
        {
            yield break;
        }

        yield return new HeadingBlock("Data quality");

        foreach (var notice in report.Notices)
        {
            yield return new NoticeBlock(
                notice.Message,
                notice.Severity switch
                {
                    NoticeSeverity.Error => DocumentTone.Negative,
                    NoticeSeverity.Warning => DocumentTone.Caution,
                    _ => DocumentTone.Neutral,
                });
        }
    }

    // --- shared ------------------------------------------------------------------------------

    /// <summary>
    /// States when a table has been cut short. Phase 23 is explicit that a document must not
    /// hide a gap, and a silently truncated table reads as a complete one.
    /// </summary>
    private static string? TruncationNote(int total)
        => total > DetailRowLimit
            ? $"Showing the {DetailRowLimit} highest-spending of {total}. The CSV export carries every row."
            : null;

    private static DocumentProvenance Provenance(AuditReport report, BrandOptions brand) => new()
    {
        ProductName = brand.ProductName,
        GeneratedAt = report.Formatter.FormatDateTime(report.GeneratedAt) + " UTC",
        DataSource = report.SourceName,
        IsDemo = report.IsDemo,
        AccountName = report.Account.AccountName,
        AccountId = report.Account.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CustomerId = report.Account.CustomerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Currency = report.Formatter.CurrencyCode,
        Period = $"{report.DateRangeLabel} ({report.Period.Current.Days} days)",
        ComparisonPeriod = report.ComparisonLabel,
        TimeZone = report.Period.TimeZoneId,
        Reconciliation = report.IsDemo
            ? "Not applicable — these figures are synthetic."
            : "These figures have not been reconciled against the Microsoft Advertising interface.",
        Methodology =
            $"{report.Recommendations.RulesEvaluated} deterministic rules were evaluated against the "
            + "retrieved figures. Every finding states the data behind it. Ratios are derived on "
            + "read rather than stored, and impression-share percentages are never summed. No "
            + "figure in this document was produced or altered by an AI service.",
    };

    private static DocumentTone ToneOf(Verdict verdict) => verdict switch
    {
        Verdict.Positive => DocumentTone.Positive,
        Verdict.Negative => DocumentTone.Negative,
        _ => DocumentTone.Neutral,
    };

    private static DocumentTone ToneOf(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => DocumentTone.Negative,
        FindingSeverity.Warning => DocumentTone.Caution,
        FindingSeverity.Positive => DocumentTone.Positive,
        _ => DocumentTone.Neutral,
    };
}
