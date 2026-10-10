using System.Globalization;
using System.Text;
using System.Text.Json;
using MsAds.Auditor.Core.Metrics;
using MsAds.Auditor.Core.Reports;

namespace MsAds.Auditor.Web.Charts;

/// <summary>One point on a trend line. A null <see cref="Value"/> breaks the line.</summary>
public sealed record TrendPoint(int Index, DateOnly Date, decimal? Value);

/// <summary>
/// A daily trend chart for a single metric, comparing the current period with the preceding one.
/// </summary>
/// <remarks>
/// <para>
/// Geometry is calculated on the server and emitted as inline SVG, so the charts need no
/// third-party charting library and render identically offline.
/// </para>
/// <para>
/// The two series use the emphasis pattern rather than a categorical palette: the current period
/// is drawn in the brand accent and the comparison period in a de-emphasis grey, dashed. The dash
/// is deliberate secondary encoding, so the two are distinguishable without relying on colour.
/// </para>
/// <para>
/// The periods are plotted against day position rather than calendar date, because the two
/// ranges cover different dates; each point's actual date is shown in the tooltip. Both series
/// share one y-axis — a second scale would make the comparison meaningless.
/// </para>
/// </remarks>
public sealed class TrendChart
{
    // Viewport and plot insets, in user units. The SVG scales responsively to its container, so
    // the width is authored close to the width it actually renders at: scaling a much wider
    // viewBox down into the column would shrink the axis labels and strokes with it.
    private const int Width = 520;
    private const int Height = 210;
    private const int PadLeft = 56;
    private const int PadRight = 14;
    private const int PadTop = 14;
    private const int PadBottom = 30;

    private const int PlotWidth = Width - PadLeft - PadRight;
    private const int PlotHeight = Height - PadTop - PadBottom;

    private TrendChart(
        MetricKey metric,
        MetricDescriptor descriptor,
        IReadOnlyList<TrendPoint> current,
        IReadOnlyList<TrendPoint> previous,
        decimal axisMax,
        MetricFormatter formatter)
    {
        Metric = metric;
        Descriptor = descriptor;
        CurrentPoints = current;
        PreviousPoints = previous;
        AxisMax = axisMax;
        Formatter = formatter;
    }

    public MetricKey Metric { get; }

    public MetricDescriptor Descriptor { get; }

    public IReadOnlyList<TrendPoint> CurrentPoints { get; }

    public IReadOnlyList<TrendPoint> PreviousPoints { get; }

    public decimal AxisMax { get; }

    public MetricFormatter Formatter { get; }

    public string ElementId => "chart-" + Metric.ToString().ToLowerInvariant();

    public bool HasComparison => PreviousPoints.Count > 0;

    /// <summary>True when the metric is undefined for every day, e.g. CPA with no conversions.</summary>
    public bool IsEmpty => CurrentPoints.All(p => p.Value is null);

    public string ViewBox => $"0 0 {Width} {Height}";

    public static TrendChart? Build(AuditReport report, MetricKey metric)
    {
        var current = Project(report.DailyCurrent, metric);
        var previous = Project(report.DailyPrevious, metric);

        if (current.Count == 0)
        {
            return null;
        }

        var values = current.Concat(previous)
            .Select(p => p.Value)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToList();

        // A zero baseline for these metrics: all are non-negative, and starting the axis above
        // zero would exaggerate day-to-day movement.
        var axisMax = NiceCeiling(values.Count == 0 ? 0m : values.Max());

        return new TrendChart(
            metric, MetricCatalogue.Describe(metric), current, previous, axisMax, report.Formatter);
    }

    private static IReadOnlyList<TrendPoint> Project(
        IReadOnlyList<Core.Models.DailyMetrics> daily, MetricKey metric)
        => [.. daily.Select((d, i) => new TrendPoint(i, d.Date, d.Metrics.Get(metric)))];

    /// <summary>Rounds the axis maximum up to a readable value so the gridline labels are tidy.</summary>
    internal static decimal NiceCeiling(decimal max)
    {
        if (max <= 0m)
        {
            return 1m;
        }

        var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)max)));
        var normalised = max / magnitude;

        var step = normalised switch
        {
            <= 1m => 1m,
            <= 2m => 2m,
            <= 2.5m => 2.5m,
            <= 5m => 5m,
            _ => 10m,
        };

        return step * magnitude;
    }

    private decimal X(int index, int count)
        => count <= 1 ? PadLeft + PlotWidth / 2m : PadLeft + index * (decimal)PlotWidth / (count - 1);

    private decimal Y(decimal value)
        => AxisMax <= 0m ? PadTop + PlotHeight : PadTop + PlotHeight * (1m - value / AxisMax);

    /// <summary>
    /// The SVG path for a series. Missing values break the line into separate subpaths rather
    /// than being drawn as zero, so a gap reads as "no value" instead of "nothing happened".
    /// </summary>
    public string PathFor(IReadOnlyList<TrendPoint> points)
    {
        var path = new StringBuilder();
        var penDown = false;

        foreach (var point in points)
        {
            if (point.Value is null)
            {
                penDown = false;
                continue;
            }

            var x = Round(X(point.Index, points.Count));
            var y = Round(Y(point.Value.Value));

            path.Append(penDown ? $" L{x},{y}" : $"{(path.Length > 0 ? " " : "")}M{x},{y}");
            penDown = true;
        }

        return path.ToString();
    }

    public string CurrentPath => PathFor(CurrentPoints);

    public string PreviousPath => PathFor(PreviousPoints);

    /// <summary>Four horizontal gridlines with their axis labels.</summary>
    public IEnumerable<(decimal Y, string Label)> GridLines()
    {
        const int lines = 4;

        for (var i = 0; i <= lines; i++)
        {
            var value = AxisMax * i / lines;
            yield return (Round(Y(value)), Formatter.Format(value, Descriptor.Format));
        }
    }

    /// <summary>
    /// Date labels for the first, middle and last day. Only three, so they cannot collide at any
    /// period length.
    /// </summary>
    public IEnumerable<(decimal X, string Label, string Anchor)> DateLabels()
    {
        if (CurrentPoints.Count == 0)
        {
            yield break;
        }

        var count = CurrentPoints.Count;
        var indices = count <= 2 ? new[] { 0, count - 1 } : [0, count / 2, count - 1];

        foreach (var index in indices.Distinct())
        {
            var anchor = index == 0 ? "start" : index == count - 1 ? "end" : "middle";
            yield return (Round(X(index, count)), CurrentPoints[index].Date.ToString("d MMM", CultureInfo.InvariantCulture), anchor);
        }
    }

    /// <summary>The x positions the hover layer snaps to.</summary>
    public IEnumerable<(int Index, decimal X)> HoverColumns()
        => CurrentPoints.Select(p => (p.Index, Round(X(p.Index, CurrentPoints.Count))));

    public decimal PlotLeft => PadLeft;

    public decimal PlotTop => PadTop;

    public decimal PlotRight => PadLeft + PlotWidth;

    public decimal PlotBottom => PadTop + PlotHeight;

    /// <summary>
    /// The tooltip payload, read by the hover script. Pre-formatted on the server so the client
    /// never re-implements the metric formatting and cannot disagree with the tables.
    /// </summary>
    public string HoverDataJson()
    {
        var previousByIndex = PreviousPoints.ToDictionary(p => p.Index);

        var rows = CurrentPoints.Select(point =>
        {
            previousByIndex.TryGetValue(point.Index, out var previous);

            return new
            {
                x = (double)Round(X(point.Index, CurrentPoints.Count)),
                currentY = point.Value is null ? (double?)null : (double)Round(Y(point.Value.Value)),
                previousY = previous?.Value is null ? (double?)null : (double)Round(Y(previous.Value.Value)),
                currentDate = point.Date.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture),
                previousDate = previous?.Date.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture),
                currentLabel = Formatter.Format(point.Value, Descriptor.Format),
                previousLabel = previous is null ? null : Formatter.Format(previous.Value, Descriptor.Format),
            };
        });

        return JsonSerializer.Serialize(rows);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2);
}

/// <summary>
/// A sparkline for a stat tile: the current period's daily values, drawn small with no axes.
/// </summary>
public sealed record Sparkline(string Path, bool HasData)
{
    private const int Width = 120;
    private const int Height = 28;

    public string ViewBox => $"0 0 {Width} {Height}";

    public static Sparkline Build(AuditReport report, MetricKey metric)
        => FromDaily(report.DailyCurrent, metric);

    /// <summary>Builds a sparkline straight from daily rows, for use outside a full report.</summary>
    public static Sparkline FromDaily(IReadOnlyList<Core.Models.DailyMetrics> daily, MetricKey metric)
    {
        var values = daily.Select(d => d.Metrics.Get(metric)).ToList();
        var present = values.Where(v => v is not null).Select(v => v!.Value).ToList();

        if (present.Count < 2)
        {
            return new Sparkline(string.Empty, false);
        }

        var min = Math.Min(0m, present.Min());
        var max = present.Max();
        var span = max - min;

        var path = new StringBuilder();
        var penDown = false;

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is null)
            {
                penDown = false;
                continue;
            }

            var x = values.Count <= 1 ? Width / 2m : i * (decimal)Width / (values.Count - 1);
            var y = span == 0m
                ? Height / 2m
                : Height - 2 - (values[i]!.Value - min) / span * (Height - 4);

            path.Append(penDown
                ? $" L{Math.Round(x, 1)},{Math.Round(y, 1)}"
                : $"{(path.Length > 0 ? " " : "")}M{Math.Round(x, 1)},{Math.Round(y, 1)}");
            penDown = true;
        }

        return new Sparkline(path.ToString(), true);
    }
}

/// <summary>
/// A chart plus the period labels used in its legend and tooltip, so the view does not have to
/// re-derive them.
/// </summary>
public sealed record TrendChartView(TrendChart Chart, string CurrentLabel, string PreviousLabel)
{
    public static IReadOnlyList<TrendChartView> ForDashboard(AuditReport report)
    {
        var currentLabel = report.DateRangeLabel;
        var previousLabel = report.ComparisonLabel;

        // The metrics the brief asks to trend: spend, clicks, conversions, CPA, and conversion
        // value or ROAS where the account records it.
        var metrics = new List<MetricKey>
        {
            MetricKey.Spend,
            MetricKey.Clicks,
            MetricKey.Conversions,
            MetricKey.CostPerAcquisition,
        };

        if (report.Current.HasConversionValueData)
        {
            metrics.Add(MetricKey.ConversionValue);
            metrics.Add(MetricKey.ReturnOnAdSpend);
        }

        return
        [
            .. metrics
                .Select(metric => TrendChart.Build(report, metric))
                .Where(chart => chart is not null)
                .Select(chart => new TrendChartView(chart!, currentLabel, previousLabel)),
        ];
    }
}
