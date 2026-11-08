using PdfSharpCore.Fonts;

namespace MsAds.Auditor.Reporting;

/// <summary>
/// Supplies the font faces PdfSharpCore embeds in a PDF, found on the host.
/// </summary>
/// <remarks>
/// <para>
/// PdfSharpCore has no fonts of its own; it asks for the bytes of a face by name. Rather than
/// commit a megabyte and a half of TTF to the repository, the faces are discovered from the
/// host's font directories. The Dockerfile installs <c>fonts-dejavu-core</c> so a container has
/// them, and every desktop Linux, macOS and Windows machine already does.
/// </para>
/// <para>
/// A wide-coverage font matters more here than a handsome one: search terms arrive in whatever
/// script the person typed, and a face without the glyph renders a blank rather than the word.
/// DejaVu is preferred for that reason, with Liberation and FreeSans as fallbacks.
/// </para>
/// <para>
/// If no usable font is found, <see cref="IsAvailable"/> is false and PDF export reports a clear
/// reason. It does not throw at start-up: the rest of the application, CSV export included, works
/// perfectly well without a font installed, and refusing to boot over it would be out of
/// proportion.
/// </para>
/// </remarks>
internal sealed class SystemFontResolver : IFontResolver
{
    /// <summary>
    /// Candidate families in preference order. Each entry names the regular and bold files; the
    /// renderer only ever asks for those two weights.
    /// </summary>
    private static readonly (string Family, string Regular, string Bold)[] Candidates =
    [
        ("DejaVu Sans", "DejaVuSans.ttf", "DejaVuSans-Bold.ttf"),
        ("Liberation Sans", "LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf"),
        ("FreeSans", "FreeSans.ttf", "FreeSansBold.ttf"),
        ("Noto Sans", "NotoSans-Regular.ttf", "NotoSans-Bold.ttf"),
        ("Arial", "arial.ttf", "arialbd.ttf"),
    ];

    private static readonly string[] SearchRoots =
    [
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        "/Library/Fonts",
        "/System/Library/Fonts",
        @"C:\Windows\Fonts",
    ];

    internal const string RegularFaceName = "report-regular";
    internal const string BoldFaceName = "report-bold";

    private readonly Dictionary<string, byte[]> _faces = new(StringComparer.Ordinal);

    internal SystemFontResolver()
    {
        var found = Discover();

        if (found is null)
        {
            return;
        }

        (FamilyName, var regular, var bold) = found.Value;

        _faces[RegularFaceName] = File.ReadAllBytes(regular);
        _faces[BoldFaceName] = File.ReadAllBytes(bold);
    }

    /// <summary>The family that was found, or null when none was.</summary>
    internal string? FamilyName { get; }

    internal bool IsAvailable => _faces.Count == 2;

    /// <summary>Why PDF export is unavailable, suitable for showing to the user.</summary>
    internal static string UnavailableReason =>
        "No suitable font was found on this machine, so a PDF cannot be rendered. Install a "
        + "DejaVu, Liberation or FreeSans font package (on Debian or Ubuntu: "
        + "`apt-get install fonts-dejavu-core`) and restart. CSV export is unaffected.";

    /// <summary>
    /// The face used when a requested family is unknown. Everything maps onto the two discovered
    /// faces anyway, so this is simply the regular one.
    /// </summary>
    public string DefaultFontName => RegularFaceName;

    public byte[]? GetFont(string faceName)
        => _faces.TryGetValue(faceName, out var bytes) ? bytes : null;

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Everything maps onto the two discovered faces. Italic is synthesised by the viewer
        // rather than carried as a third file: nothing in these documents relies on it.
        _ = familyName;
        _ = isItalic;

        if (!IsAvailable)
        {
            return null;
        }

        return new FontResolverInfo(isBold ? BoldFaceName : RegularFaceName);
    }

    /// <summary>
    /// Finds the first candidate family whose regular and bold files both exist.
    /// </summary>
    private static (string Family, string Regular, string Bold)? Discover()
    {
        var roots = SearchRoots.Where(Directory.Exists).ToList();

        if (roots.Count == 0)
        {
            return null;
        }

        // One directory walk, then a lookup per candidate — rather than a walk per candidate,
        // which on a machine with many fonts is noticeably slow at start-up.
        var byFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            foreach (var file in SafeEnumerate(root))
            {
                var name = Path.GetFileName(file);

                // First match wins, so the preference order of the roots is respected.
                byFileName.TryAdd(name, file);
            }
        }

        foreach (var (family, regular, bold) in Candidates)
        {
            if (byFileName.TryGetValue(regular, out var regularPath)
                && byFileName.TryGetValue(bold, out var boldPath))
            {
                return (family, regularPath, boldPath);
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerates .ttf files beneath a root, ignoring directories the process cannot read.
    /// </summary>
    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.ttf", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 6,
            });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
