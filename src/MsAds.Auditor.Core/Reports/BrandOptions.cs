namespace MsAds.Auditor.Core.Reports;

/// <summary>
/// The company's brand values, in a form an artefact renderer can read.
/// </summary>
/// <remarks>
/// <para>
/// Phase 23 of ROADMAP.md asks for "branding configuration to prevent hardcoded theme values",
/// and Phase 16 for "one brand implementation for Razor Pages, email, client portal, and
/// downloadable reports". The interface already keeps every brand value in a single
/// <c>:root</c> block in <c>wwwroot/css/site.css</c>; a PDF cannot read CSS, so the values are
/// restated here.
/// </para>
/// <para>
/// Restating them is a duplication, and duplication drifts. A test parses <c>site.css</c> and
/// fails if the two disagree, so changing a brand colour in the stylesheet without changing it
/// here breaks the build rather than quietly producing a PDF in last year's colours.
/// </para>
/// <para>
/// Every value is overridable from configuration, so an agency rebrand is a settings change
/// rather than a recompile.
/// </para>
/// </remarks>
public sealed record BrandOptions
{
    public const string SectionName = "Auditor:Brand";

    /// <summary>What the artefacts call the product.</summary>
    public string ProductName { get; init; } = "Microsoft Ads Performance Auditor";

    /// <summary>The agency name, shown in the footer.</summary>
    public string OrganisationName { get; init; } = "Add People";

    // --- Palette. The hex values mirror the :root block in wwwroot/css/site.css. --------------

    /// <summary>The primary brand colour, used for headings and rules.</summary>
    public string PrimaryHex { get; init; } = "#1769AA";

    /// <summary>The darker brand shade, used for the title block.</summary>
    public string DarkHex { get; init; } = "#123B5D";

    /// <summary>A pale tint, used for table header fills and banded rows.</summary>
    public string AccentLightHex { get; init; } = "#EAF3FA";

    public string TextHex { get; init; } = "#1F2937";

    public string MutedTextHex { get; init; } = "#64748B";

    public string BorderHex { get; init; } = "#DCE5EF";

    /// <summary>Status colours. Each is paired with a word in the output, never used alone.</summary>
    public string PositiveHex { get; init; } = "#136B33";

    public string NegativeHex { get; init; } = "#B42318";

    public string WarningHex { get; init; } = "#B54708";

    /// <summary>
    /// Font family for the artefacts. A font with wide coverage is needed because search terms
    /// arrive in whatever script the searcher typed.
    /// </summary>
    public string FontFamily { get; init; } = "DejaVu Sans";

    /// <summary>
    /// The names of the CSS custom properties each value mirrors, so the drift test knows what to
    /// compare without hardcoding the mapping twice.
    /// </summary>
    public IReadOnlyDictionary<string, string> CssVariableMap => new Dictionary<string, string>
    {
        ["--brand-primary"] = PrimaryHex,
        ["--brand-dark"] = DarkHex,
        ["--accent-light"] = AccentLightHex,
        ["--text"] = TextHex,
        ["--text-muted"] = MutedTextHex,
        ["--border"] = BorderHex,
        ["--positive"] = PositiveHex,
        ["--negative"] = NegativeHex,
        ["--warning"] = WarningHex,
    };
}
