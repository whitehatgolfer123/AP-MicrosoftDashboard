namespace MsAds.Auditor.Core.Reports.Documents;

/// <summary>How a table column's values are aligned.</summary>
public enum ColumnAlignment
{
    Left,
    Right,
}

/// <summary>The weight a notice or verdict carries, so a renderer can colour it consistently.</summary>
public enum DocumentTone
{
    Neutral,
    Positive,
    Caution,
    Negative,
}

/// <summary>One element of a renderable document.</summary>
/// <remarks>
/// A closed set of blocks rather than free-form markup. The point of Phase 23's "one shared
/// content model" is that a PDF and the dashboard cannot disagree: the content and the
/// calculations are decided once, here, and the renderer only decides how to draw a heading or a
/// table. A renderer that cannot handle a block type is a compile error, not a blank page.
/// </remarks>
public abstract record DocumentBlock;

/// <summary>A section heading. Level 1 is a major section, 2 a subsection.</summary>
public sealed record HeadingBlock(string Text, int Level = 1) : DocumentBlock;

public sealed record ParagraphBlock(string Text, DocumentTone Tone = DocumentTone.Neutral) : DocumentBlock;

public sealed record BulletsBlock(IReadOnlyList<string> Items) : DocumentBlock;

/// <summary>Label and value pairs, for provenance and summary panels.</summary>
public sealed record KeyValuesBlock(IReadOnlyList<KeyValuePair<string, string>> Rows) : DocumentBlock;

/// <summary>A headline figure with its period-on-period movement.</summary>
public sealed record MetricCard(
    string Label,
    string Value,
    string Change,
    DocumentTone Tone);

public sealed record MetricCardsBlock(IReadOnlyList<MetricCard> Cards) : DocumentBlock;

public sealed record TableBlock(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<ColumnAlignment> Alignments,
    string? EmptyMessage = null) : DocumentBlock;

/// <summary>
/// One finding, with everything the reader needs to weigh it: why it was flagged, the figures
/// behind it, what to consider doing, and the caveats.
/// </summary>
public sealed record FindingBlock(
    string RuleId,
    string Title,
    string Severity,
    string Confidence,
    string Category,
    string? Entity,
    string Reason,
    string SuggestedAction,
    IReadOnlyList<KeyValuePair<string, string>> Evidence,
    IReadOnlyList<string> Caveats,
    DocumentTone Tone) : DocumentBlock;

/// <summary>A data-quality notice. Rendered so it cannot be mistaken for a finding.</summary>
public sealed record NoticeBlock(string Text, DocumentTone Tone) : DocumentBlock;

/// <summary>Starts a new page. Used between an executive summary and the detail behind it.</summary>
public sealed record PageBreakBlock : DocumentBlock;

/// <summary>
/// Where the figures in a document came from, so a printed page is never unattributable.
/// </summary>
/// <remarks>
/// Phase 23 lists what must appear: the account identifier, currency, period, data source,
/// demo or live status, reconciliation state, generated time and methodology. They are a
/// required part of the document rather than an optional footer, because a PDF outlives the
/// screen it was generated from and will be read by someone who was not there.
/// </remarks>
public sealed record DocumentProvenance
{
    public required string ProductName { get; init; }

    public required string GeneratedAt { get; init; }

    /// <summary>"Microsoft Advertising API" or "Demo data".</summary>
    public required string DataSource { get; init; }

    /// <summary>
    /// True when the figures are synthetic. Stated prominently, not only in the footer: a demo
    /// PDF that reaches a client without that on its face is the worst outcome this carries.
    /// </summary>
    public required bool IsDemo { get; init; }

    public string? AccountName { get; init; }

    public string? AccountId { get; init; }

    public string? CustomerId { get; init; }

    public string? Currency { get; init; }

    public string? Period { get; init; }

    public string? ComparisonPeriod { get; init; }

    public string? TimeZone { get; init; }

    /// <summary>
    /// Whether the figures have been reconciled against the Microsoft Advertising interface.
    /// Until that exercise has been done the honest answer is that they have not been.
    /// </summary>
    public required string Reconciliation { get; init; }

    /// <summary>How the findings were produced, in a sentence or two.</summary>
    public required string Methodology { get; init; }

    /// <summary>The provenance block as label and value rows, in reading order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Rows()
    {
        var rows = new List<KeyValuePair<string, string>>();

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new KeyValuePair<string, string>(label, value));
            }
        }

        Add("Account", AccountName is null ? null : $"{AccountName}{(AccountId is null ? "" : $" ({AccountId})")}");
        Add("Manager (customer) id", CustomerId);
        Add("Reporting period", Period);
        Add("Comparison period", ComparisonPeriod);
        Add("Reporting time zone", TimeZone);
        Add("Currency", Currency);
        // The marker is appended only when the source name does not already say so, or a demo
        // source reads "Demo data (synthetic) — SYNTHETIC DEMO FIGURES".
        var saysSoAlready = IsDemo
            && (DataSource.Contains("synthetic", StringComparison.OrdinalIgnoreCase)
                || DataSource.Contains("demo", StringComparison.OrdinalIgnoreCase));

        Add("Data source", DataSource + (IsDemo && !saysSoAlready ? " — SYNTHETIC DEMO FIGURES" : ""));
        Add("Reconciliation", Reconciliation);
        Add("Generated", GeneratedAt);

        return rows;
    }
}

/// <summary>
/// A complete document, ready for any renderer.
/// </summary>
public sealed record ReportDocument
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    /// <summary>The download file name, without an extension.</summary>
    public required string FileStem { get; init; }

    public required DocumentProvenance Provenance { get; init; }

    public IReadOnlyList<DocumentBlock> Blocks { get; init; } = [];
}
