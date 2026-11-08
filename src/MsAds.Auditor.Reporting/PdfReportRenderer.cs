using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Reports;
using MsAds.Auditor.Core.Reports.Documents;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace MsAds.Auditor.Reporting;

/// <summary>Renders a <see cref="ReportDocument"/> as a PDF.</summary>
public interface IReportRenderer
{
    /// <summary>False when no font could be found, in which case rendering is impossible.</summary>
    bool CanRender { get; }

    /// <summary>Why rendering is unavailable. Null when <see cref="CanRender"/> is true.</summary>
    string? UnavailableReason { get; }

    byte[] Render(ReportDocument document);
}

/// <summary>
/// Draws a document onto A4 pages.
/// </summary>
/// <remarks>
/// <para>
/// PdfSharpCore draws; it does not lay out. Everything below — wrapping, pagination, column
/// widths, repeating a table header across a page break — is this class's job. It is kept
/// deliberately small and block-at-a-time: each block measures itself, asks for a page break if
/// it will not fit, then draws.
/// </para>
/// <para>
/// The renderer makes no content decisions. Which figures appear, how they are formatted and
/// what the document says are all settled in <see cref="AuditDocumentBuilder"/>, so a PDF cannot
/// disagree with the dashboard.
/// </para>
/// </remarks>
public sealed class PdfReportRenderer : IReportRenderer
{
    // A4 at 72dpi, with margins wide enough to print without clipping.
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double MarginLeft = 48;
    private const double MarginRight = 48;
    private const double MarginTop = 54;
    private const double MarginBottom = 56;

    private const double ContentWidth = PageWidth - MarginLeft - MarginRight;

    private static readonly object FontResolverGate = new();
    private static SystemFontResolver? _sharedResolver;

    private readonly BrandOptions _brand;
    private readonly ILogger<PdfReportRenderer> _logger;
    private readonly SystemFontResolver _fonts;

    public PdfReportRenderer(IOptions<BrandOptions> brand, ILogger<PdfReportRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(brand);

        _brand = brand.Value;
        _logger = logger;
        _fonts = SharedResolver();

        if (_fonts.IsAvailable)
        {
            _logger.LogInformation(
                "PDF export is available using the {Family} font family.", _fonts.FamilyName);
        }
        else
        {
            _logger.LogWarning("{Reason}", SystemFontResolver.UnavailableReason);
        }
    }

    public bool CanRender => _fonts.IsAvailable;

    public string? UnavailableReason =>
        _fonts.IsAvailable ? null : SystemFontResolver.UnavailableReason;

    /// <summary>
    /// PdfSharpCore's font resolver is a process-wide static, so it is set once and shared. A
    /// second assignment with a different instance would make rendering depend on which
    /// renderer happened to be constructed last.
    /// </summary>
    private static SystemFontResolver SharedResolver()
    {
        lock (FontResolverGate)
        {
            if (_sharedResolver is null)
            {
                _sharedResolver = new SystemFontResolver();
                GlobalFontSettings.FontResolver = _sharedResolver;
            }

            return _sharedResolver;
        }
    }

    public byte[] Render(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!CanRender)
        {
            throw new InvalidOperationException(SystemFontResolver.UnavailableReason);
        }

        using var pdf = new PdfDocument();

        pdf.Info.Title = $"{document.Title} — {document.Subtitle}";
        pdf.Info.Author = _brand.OrganisationName;
        pdf.Info.Creator = _brand.ProductName;

        // Stated in the metadata as well as on the page: a demo PDF that reaches a client
        // should be identifiable however it is inspected.
        pdf.Info.Subject = document.Provenance.IsDemo
            ? "SYNTHETIC DEMO FIGURES — not live advertising data"
            : document.Provenance.Period ?? string.Empty;

        var canvas = new Canvas(pdf, _brand, document);

        canvas.DrawTitlePage();

        foreach (var block in document.Blocks)
        {
            canvas.Draw(block);
        }

        canvas.Finish();

        using var buffer = new MemoryStream();
        pdf.Save(buffer, closeStream: false);

        var bytes = buffer.ToArray();

        _logger.LogInformation(
            "Rendered {Title} for {Subtitle} as {Pages} page(s), {Bytes} bytes",
            document.Title, document.Subtitle, pdf.PageCount, bytes.Length);

        return bytes;
    }

    /// <summary>
    /// Holds the current page and cursor, and knows how to draw each block type.
    /// </summary>
    private sealed class Canvas
    {
        private const double BodySize = 9.5;
        private const double SmallSize = 8;
        private const double Leading = 1.35;

        private readonly PdfDocument _pdf;
        private readonly BrandOptions _brand;
        private readonly ReportDocument _document;

        private readonly XFont _body;
        private readonly XFont _bodyBold;
        private readonly XFont _small;
        private readonly XFont _smallBold;
        private readonly XFont _h1;
        private readonly XFont _h2;
        private readonly XFont _titleFont;

        private readonly XColor _primary;
        private readonly XColor _dark;
        private readonly XColor _accent;
        private readonly XColor _text;
        private readonly XColor _muted;
        private readonly XColor _border;
        private readonly XColor _positive;
        private readonly XColor _negative;
        private readonly XColor _warning;

        private XGraphics _gfx = null!;
        private double _y;
        private int _pageNumber;

        internal Canvas(PdfDocument pdf, BrandOptions brand, ReportDocument document)
        {
            _pdf = pdf;
            _brand = brand;
            _document = document;

            // The family name is ignored by the resolver, which maps everything onto the two
            // discovered faces; it still has to be something non-empty.
            const string Family = "report";

            _body = new XFont(Family, BodySize, XFontStyle.Regular);
            _bodyBold = new XFont(Family, BodySize, XFontStyle.Bold);
            _small = new XFont(Family, SmallSize, XFontStyle.Regular);
            _smallBold = new XFont(Family, SmallSize, XFontStyle.Bold);
            _h1 = new XFont(Family, 14, XFontStyle.Bold);
            _h2 = new XFont(Family, 11, XFontStyle.Bold);
            _titleFont = new XFont(Family, 22, XFontStyle.Bold);

            _primary = Colour(brand.PrimaryHex);
            _dark = Colour(brand.DarkHex);
            _accent = Colour(brand.AccentLightHex);
            _text = Colour(brand.TextHex);
            _muted = Colour(brand.MutedTextHex);
            _border = Colour(brand.BorderHex);
            _positive = Colour(brand.PositiveHex);
            _negative = Colour(brand.NegativeHex);
            _warning = Colour(brand.WarningHex);

            NewPage();
        }

        // --- page management -----------------------------------------------------------------

        private void NewPage()
        {
            var page = _pdf.AddPage();
            page.Width = PageWidth;
            page.Height = PageHeight;

            _gfx?.Dispose();
            _gfx = XGraphics.FromPdfPage(page);
            _pageNumber++;
            _y = MarginTop;

            DrawRunningHeader();
        }

        /// <summary>
        /// Starts a new page if <paramref name="needed"/> points would not fit on this one.
        /// </summary>
        private void EnsureRoom(double needed)
        {
            if (_y + needed > PageHeight - MarginBottom)
            {
                NewPage();
            }
        }

        private void DrawRunningHeader()
        {
            if (_pageNumber == 1)
            {
                return;
            }

            _gfx.DrawString(
                $"{_document.Title} — {_document.Subtitle}",
                _small, new XSolidBrush(_muted),
                new XRect(MarginLeft, 22, ContentWidth, 12),
                XStringFormats.CenterLeft);

            if (_document.Provenance.IsDemo)
            {
                _gfx.DrawString(
                    "DEMO DATA",
                    _smallBold, new XSolidBrush(_warning),
                    new XRect(MarginLeft, 22, ContentWidth, 12),
                    XStringFormats.CenterRight);
            }

            _gfx.DrawLine(new XPen(_border, 0.5), MarginLeft, 36, PageWidth - MarginRight, 36);
            _y = MarginTop;
        }

        /// <summary>
        /// Writes the footers once every page exists, so "page 2 of 7" can state the real total.
        /// </summary>
        internal void Finish()
        {
            _gfx?.Dispose();
            _gfx = null!;

            for (var i = 0; i < _pdf.PageCount; i++)
            {
                using var gfx = XGraphics.FromPdfPage(_pdf.Pages[i]);

                var y = PageHeight - MarginBottom + 22;

                gfx.DrawLine(new XPen(_border, 0.5), MarginLeft, y - 10, PageWidth - MarginRight, y - 10);

                gfx.DrawString(
                    _document.Provenance.IsDemo
                        ? $"{_brand.OrganisationName} — {_brand.ProductName} — SYNTHETIC DEMO FIGURES"
                        : $"{_brand.OrganisationName} — {_brand.ProductName}",
                    _small, new XSolidBrush(_muted),
                    new XRect(MarginLeft, y, ContentWidth, 12),
                    XStringFormats.CenterLeft);

                gfx.DrawString(
                    string.Create(
                        CultureInfo.InvariantCulture, $"Page {i + 1} of {_pdf.PageCount}"),
                    _small, new XSolidBrush(_muted),
                    new XRect(MarginLeft, y, ContentWidth, 12),
                    XStringFormats.CenterRight);
            }
        }

        // --- title page ----------------------------------------------------------------------

        internal void DrawTitlePage()
        {
            _gfx.DrawRectangle(new XSolidBrush(_dark), 0, 0, PageWidth, 150);

            _gfx.DrawString(
                _brand.ProductName.ToUpperInvariant(),
                _smallBold, XBrushes.White,
                new XRect(MarginLeft, 36, ContentWidth, 14),
                XStringFormats.CenterLeft);

            _gfx.DrawString(
                _document.Title,
                _titleFont, XBrushes.White,
                new XRect(MarginLeft, 62, ContentWidth, 30),
                XStringFormats.CenterLeft);

            _gfx.DrawString(
                _document.Subtitle,
                _body, XBrushes.White,
                new XRect(MarginLeft, 100, ContentWidth, 16),
                XStringFormats.CenterLeft);

            _y = 176;

            if (_document.Provenance.IsDemo)
            {
                // On the face of the document, not only in the footer. A demo PDF that reaches a
                // client without this is the worst thing this feature could do.
                DrawBanner(
                    "These figures are synthetic demo data, not live advertising performance. "
                    + "Do not send this document to a client.",
                    _warning);
            }

            DrawHeading(new HeadingBlock("About this report"));
            DrawKeyValues(new KeyValuesBlock(_document.Provenance.Rows()));

            DrawHeading(new HeadingBlock("Methodology", 2));
            DrawParagraph(new ParagraphBlock(_document.Provenance.Methodology));

            NewPage();
        }

        // --- block dispatch ------------------------------------------------------------------

        internal void Draw(DocumentBlock block)
        {
            switch (block)
            {
                case HeadingBlock heading: DrawHeading(heading); break;
                case ParagraphBlock paragraph: DrawParagraph(paragraph); break;
                case BulletsBlock bullets: DrawBullets(bullets); break;
                case KeyValuesBlock pairs: DrawKeyValues(pairs); break;
                case MetricCardsBlock cards: DrawMetricCards(cards); break;
                case TableBlock table: DrawTable(table); break;
                case FindingBlock finding: DrawFinding(finding); break;
                case NoticeBlock notice: DrawNotice(notice); break;
                case PageBreakBlock: NewPage(); break;

                // A new block type must be handled here rather than silently omitted, which
                // would leave a gap in a document nobody notices until a client asks.
                default:
                    throw new NotSupportedException(
                        $"No renderer for document block {block.GetType().Name}.");
            }
        }

        // --- blocks --------------------------------------------------------------------------

        private void DrawHeading(HeadingBlock heading)
        {
            var font = heading.Level <= 1 ? _h1 : _h2;
            var height = heading.Level <= 1 ? 22d : 18d;

            // Kept with at least a couple of lines of what follows, so a heading never sits
            // alone at the foot of a page.
            EnsureRoom(height + 28);

            _y += heading.Level <= 1 ? 10 : 8;

            _gfx.DrawString(
                heading.Text, font, new XSolidBrush(heading.Level <= 1 ? _primary : _text),
                new XRect(MarginLeft, _y, ContentWidth, height),
                XStringFormats.TopLeft);

            _y += height;

            if (heading.Level <= 1)
            {
                _gfx.DrawLine(new XPen(_primary, 1), MarginLeft, _y, PageWidth - MarginRight, _y);
                _y += 8;
            }
            else
            {
                _y += 2;
            }
        }

        private void DrawParagraph(ParagraphBlock paragraph)
            => DrawWrapped(paragraph.Text, _body, BrushFor(paragraph.Tone), MarginLeft, ContentWidth);

        private void DrawBullets(BulletsBlock bullets)
        {
            foreach (var item in bullets.Items)
            {
                var lines = WrapCount(item, _body, ContentWidth - 14);
                EnsureRoom(lines * BodySize * Leading + 4);

                _gfx.DrawString(
                    "•", _body, new XSolidBrush(_primary),
                    new XRect(MarginLeft, _y, 10, BodySize * Leading),
                    XStringFormats.TopLeft);

                DrawWrapped(item, _body, new XSolidBrush(_text), MarginLeft + 14, ContentWidth - 14);
                _y += 2;
            }
        }

        private void DrawKeyValues(KeyValuesBlock pairs)
        {
            const double LabelWidth = 150;

            foreach (var (label, value) in pairs.Rows)
            {
                var lines = WrapCount(value, _body, ContentWidth - LabelWidth - 8);
                var height = Math.Max(BodySize * Leading, lines * BodySize * Leading);

                EnsureRoom(height + 4);

                _gfx.DrawString(
                    label, _bodyBold, new XSolidBrush(_muted),
                    new XRect(MarginLeft, _y, LabelWidth, BodySize * Leading),
                    XStringFormats.TopLeft);

                var startY = _y;
                DrawWrapped(value, _body, new XSolidBrush(_text), MarginLeft + LabelWidth, ContentWidth - LabelWidth);
                _y = Math.Max(_y, startY + height) + 3;
            }

            _y += 4;
        }

        private void DrawMetricCards(MetricCardsBlock block)
        {
            const int PerRow = 4;
            const double Gap = 8;
            const double CardHeight = 54;

            var cardWidth = (ContentWidth - (Gap * (PerRow - 1))) / PerRow;

            for (var i = 0; i < block.Cards.Count; i += PerRow)
            {
                EnsureRoom(CardHeight + Gap);

                var row = block.Cards.Skip(i).Take(PerRow).ToList();

                for (var column = 0; column < row.Count; column++)
                {
                    var card = row[column];
                    var x = MarginLeft + (column * (cardWidth + Gap));

                    _gfx.DrawRectangle(
                        new XPen(_border, 0.75), new XSolidBrush(_accent),
                        x, _y, cardWidth, CardHeight);

                    _gfx.DrawString(
                        card.Label, _small, new XSolidBrush(_muted),
                        new XRect(x + 7, _y + 6, cardWidth - 14, 11),
                        XStringFormats.CenterLeft);

                    _gfx.DrawString(
                        card.Value, _bodyBold, new XSolidBrush(_text),
                        new XRect(x + 7, _y + 20, cardWidth - 14, 16),
                        XStringFormats.CenterLeft);

                    // The movement carries a colour, but the words come from the formatter —
                    // "New activity", "Stopped", "No change" — so colour is never the only
                    // signal.
                    _gfx.DrawString(
                        card.Change, _small, BrushFor(card.Tone),
                        new XRect(x + 7, _y + 37, cardWidth - 14, 11),
                        XStringFormats.CenterLeft);
                }

                _y += CardHeight + Gap;
            }

            _y += 2;
        }

        private void DrawTable(TableBlock table)
        {
            const double RowPadding = 4;
            const double HeaderHeight = 18;

            if (table.Rows.Count == 0)
            {
                if (table.EmptyMessage is not null)
                {
                    DrawNotice(new NoticeBlock(table.EmptyMessage, DocumentTone.Neutral));
                }

                return;
            }

            var widths = ColumnWidths(table);

            void DrawHeaderRow()
            {
                _gfx.DrawRectangle(
                    new XSolidBrush(_accent), MarginLeft, _y, ContentWidth, HeaderHeight);

                var x = MarginLeft;

                for (var c = 0; c < table.Headers.Count; c++)
                {
                    _gfx.DrawString(
                        table.Headers[c], _smallBold, new XSolidBrush(_dark),
                        new XRect(x + RowPadding, _y, widths[c] - (RowPadding * 2), HeaderHeight),
                        Align(table.Alignments, c));

                    x += widths[c];
                }

                _y += HeaderHeight;
            }

            EnsureRoom(HeaderHeight + 40);
            DrawHeaderRow();

            var banded = false;

            foreach (var row in table.Rows)
            {
                // The tallest wrapped cell sets the row height.
                var lines = 1;

                for (var c = 0; c < row.Count && c < widths.Count; c++)
                {
                    lines = Math.Max(lines, WrapCount(row[c], _small, widths[c] - (RowPadding * 2)));
                }

                var rowHeight = (lines * SmallSize * Leading) + (RowPadding * 1.5);

                if (_y + rowHeight > PageHeight - MarginBottom)
                {
                    NewPage();

                    // The header is repeated, or the continuation reads as unlabelled numbers.
                    DrawHeaderRow();
                    banded = false;
                }

                if (banded)
                {
                    _gfx.DrawRectangle(
                        new XSolidBrush(XColor.FromArgb(10, 0, 0, 0)),
                        MarginLeft, _y, ContentWidth, rowHeight);
                }

                banded = !banded;

                var x = MarginLeft;

                for (var c = 0; c < row.Count && c < widths.Count; c++)
                {
                    DrawCell(
                        row[c], x + RowPadding, widths[c] - (RowPadding * 2), rowHeight,
                        Align(table.Alignments, c));

                    x += widths[c];
                }

                _gfx.DrawLine(
                    new XPen(_border, 0.4),
                    MarginLeft, _y + rowHeight, PageWidth - MarginRight, _y + rowHeight);

                _y += rowHeight;
            }

            _y += 6;

            // A truncation note belongs with the table it truncates, so it is drawn here rather
            // than left to the caller to remember.
            if (table.EmptyMessage is not null)
            {
                DrawNotice(new NoticeBlock(table.EmptyMessage, DocumentTone.Neutral));
            }
        }

        /// <summary>
        /// Draws one table cell, wrapping to the column width.
        /// </summary>
        /// <remarks>
        /// Hand-drawn line by line rather than handed to XTextFormatter. XTextFormatter
        /// distributes the words of a wrapped line across the full column width — "Shopping –
        /// Boilers &amp; parts" came out spread from one edge to the other — which made every
        /// multi-word cell look justified and unreadable. Reusing WrapLines keeps cells
        /// consistent with the rest of the document, which already measures its own text.
        /// </remarks>
        private void DrawCell(string value, double x, double width, double height, XStringFormat format)
        {
            _ = height;

            var lineHeight = SmallSize * Leading;
            var top = _y + 3;

            foreach (var line in WrapLines(value, _small, width))
            {
                _gfx.DrawString(
                    line, _small, new XSolidBrush(_text),
                    new XRect(x, top, width, lineHeight),
                    format);

                top += lineHeight;
            }
        }

        private void DrawFinding(FindingBlock finding)
        {
            EnsureRoom(70);

            var accent = BrushFor(finding.Tone);

            _y += 6;

            // A coloured bar down the left edge, with the severity also spelled out beside the
            // title: the tone is never the only indication of how serious a finding is.
            var top = _y;

            _gfx.DrawString(
                finding.Title, _bodyBold, new XSolidBrush(_text),
                new XRect(MarginLeft + 10, _y, ContentWidth - 10, 15),
                XStringFormats.TopLeft);

            _y += 15;

            _gfx.DrawString(
                $"{finding.Severity} · {finding.Confidence} confidence · {finding.Category}"
                + (finding.Entity is null ? "" : $" · {finding.Entity}"),
                _small, accent,
                new XRect(MarginLeft + 10, _y, ContentWidth - 10, 12),
                XStringFormats.TopLeft);

            _y += 14;

            DrawWrapped(finding.Reason, _small, new XSolidBrush(_text), MarginLeft + 10, ContentWidth - 10);
            _y += 3;

            if (finding.Evidence.Count > 0)
            {
                var evidence = string.Join(
                    "   ", finding.Evidence.Select(e => $"{e.Key}: {e.Value}"));

                DrawWrapped(evidence, _small, new XSolidBrush(_muted), MarginLeft + 10, ContentWidth - 10);
                _y += 3;
            }

            DrawWrapped(
                $"Consider: {finding.SuggestedAction}",
                _small, new XSolidBrush(_dark), MarginLeft + 10, ContentWidth - 10);

            foreach (var caveat in finding.Caveats)
            {
                _y += 2;
                DrawWrapped(
                    $"Caveat: {caveat}", _small, new XSolidBrush(_muted),
                    MarginLeft + 10, ContentWidth - 10);
            }

            _gfx.DrawRectangle(
                new XSolidBrush(((XSolidBrush)accent).Color),
                MarginLeft, top, 3, Math.Max(12, _y - top));

            _y += 8;
        }

        private void DrawNotice(NoticeBlock notice)
        {
            var lines = WrapCount(notice.Text, _small, ContentWidth - 18);
            var height = (lines * SmallSize * Leading) + 10;

            EnsureRoom(height + 6);

            var fill = notice.Tone switch
            {
                DocumentTone.Negative => XColor.FromArgb(22, 180, 35, 24),
                DocumentTone.Caution => XColor.FromArgb(22, 181, 71, 8),
                DocumentTone.Positive => XColor.FromArgb(22, 19, 107, 51),
                _ => _accent,
            };

            _gfx.DrawRectangle(new XSolidBrush(fill), MarginLeft, _y, ContentWidth, height);

            var startY = _y;
            _y += 5;

            DrawWrapped(notice.Text, _small, BrushFor(notice.Tone), MarginLeft + 9, ContentWidth - 18);

            _y = Math.Max(_y, startY + height) + 6;
        }

        private void DrawBanner(string text, XColor colour)
        {
            var lines = WrapCount(text, _bodyBold, ContentWidth - 20);
            var height = (lines * BodySize * Leading) + 14;

            EnsureRoom(height + 10);

            _gfx.DrawRectangle(
                new XPen(colour, 1.2), new XSolidBrush(XColor.FromArgb(28, colour.R, colour.G, colour.B)),
                MarginLeft, _y, ContentWidth, height);

            var startY = _y;
            _y += 7;

            DrawWrapped(text, _bodyBold, new XSolidBrush(colour), MarginLeft + 10, ContentWidth - 20);

            _y = Math.Max(_y, startY + height) + 10;
        }

        // --- text measurement ----------------------------------------------------------------

        /// <summary>
        /// Draws text wrapped to a width, paginating mid-paragraph where necessary, and leaves
        /// the cursor below it.
        /// </summary>
        private void DrawWrapped(string text, XFont font, XBrush brush, double x, double width)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var lineHeight = font.Size * Leading;

            foreach (var line in WrapLines(text, font, width))
            {
                EnsureRoom(lineHeight);

                _gfx.DrawString(
                    line, font, brush,
                    new XRect(x, _y, width, lineHeight),
                    XStringFormats.TopLeft);

                _y += lineHeight;
            }
        }

        private int WrapCount(string text, XFont font, double width)
            => string.IsNullOrEmpty(text) ? 1 : WrapLines(text, font, width).Count;

        /// <summary>
        /// Breaks text into lines that fit a width, measuring with the real font metrics.
        /// </summary>
        /// <remarks>
        /// A word longer than the whole line — a long URL in a search term, typically — is split
        /// character by character rather than allowed to overflow the column.
        /// </remarks>
        private List<string> WrapLines(string text, XFont font, double width)
        {
            var lines = new List<string>();

            if (width <= 0)
            {
                return [text];
            }

            foreach (var paragraph in text.Split('\n'))
            {
                var current = string.Empty;

                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = current.Length == 0 ? word : current + " " + word;

                    if (_gfx.MeasureString(candidate, font).Width <= width)
                    {
                        current = candidate;
                        continue;
                    }

                    if (current.Length > 0)
                    {
                        lines.Add(current);
                        current = string.Empty;
                    }

                    if (_gfx.MeasureString(word, font).Width <= width)
                    {
                        current = word;
                        continue;
                    }

                    // The word alone does not fit; break it.
                    var chunk = string.Empty;

                    foreach (var character in word)
                    {
                        if (_gfx.MeasureString(chunk + character, font).Width > width && chunk.Length > 0)
                        {
                            lines.Add(chunk);
                            chunk = string.Empty;
                        }

                        chunk += character;
                    }

                    current = chunk;
                }

                lines.Add(current);
            }

            return lines.Count == 0 ? [string.Empty] : lines;
        }

        /// <summary>
        /// Shares the content width between columns in proportion to the widest value in each,
        /// so a column of short numbers does not get the same space as one of search terms.
        /// </summary>
        private List<double> ColumnWidths(TableBlock table)
        {
            var count = table.Headers.Count;
            var natural = new double[count];

            for (var c = 0; c < count; c++)
            {
                natural[c] = _gfx.MeasureString(table.Headers[c], _smallBold).Width + 14;
            }

            foreach (var row in table.Rows)
            {
                for (var c = 0; c < count && c < row.Count; c++)
                {
                    natural[c] = Math.Max(
                        natural[c], _gfx.MeasureString(row[c], _small).Width + 14);
                }
            }

            var total = natural.Sum();

            if (total <= ContentWidth)
            {
                // Spare room goes to the first column, which is the name or the search term.
                var widths = natural.ToList();
                widths[0] += ContentWidth - total;
                return widths;
            }

            // Over-wide: scale proportionally, but keep every column readable.
            var minimum = Math.Min(44d, ContentWidth / count);
            var scaled = natural.Select(w => Math.Max(minimum, w / total * ContentWidth)).ToList();

            var scaledTotal = scaled.Sum();

            // Scaling against a floor can overshoot, so normalise once more.
            if (scaledTotal > ContentWidth)
            {
                var excess = scaledTotal - ContentWidth;
                var widest = scaled.IndexOf(scaled.Max());
                scaled[widest] = Math.Max(minimum, scaled[widest] - excess);
            }

            return scaled;
        }

        private static XStringFormat Align(IReadOnlyList<ColumnAlignment> alignments, int column)
            => column < alignments.Count && alignments[column] == ColumnAlignment.Right
                ? XStringFormats.TopRight
                : XStringFormats.TopLeft;

        private XBrush BrushFor(DocumentTone tone) => tone switch
        {
            DocumentTone.Positive => new XSolidBrush(_positive),
            DocumentTone.Negative => new XSolidBrush(_negative),
            DocumentTone.Caution => new XSolidBrush(_warning),
            _ => new XSolidBrush(_text),
        };

        private static XColor Colour(string hex)
        {
            var value = hex.TrimStart('#');

            return XColor.FromArgb(
                int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }
}
