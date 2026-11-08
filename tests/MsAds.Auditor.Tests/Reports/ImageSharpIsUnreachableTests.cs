using System.Text.RegularExpressions;

namespace MsAds.Auditor.Tests.Reports;

/// <summary>
/// Keeps the ImageSharp advisory suppressions honest.
/// </summary>
/// <remarks>
/// <para>
/// PdfSharpCore depends on SixLabors.ImageSharp, whose 2.1.x line carries three high and two
/// moderate severity advisories with no Apache-2.0 fix available — the fixes are in ImageSharp 3,
/// which moved to a licence requiring payment above a revenue threshold.
/// </para>
/// <para>
/// Those advisories are suppressed in the project files on one ground: every one is in an image
/// decoder, and this application never hands ImageSharp an image. That is a statement about the
/// code, and statements about code go stale. This test is what stops it going stale silently: if
/// a logo or a chart image is ever drawn into a report, the suppression's justification no longer
/// holds, and this fails rather than the suppression quietly covering a now-reachable
/// vulnerability.
/// </para>
/// <para>
/// If this test fails, the fix is not to delete it. It is to decide whether to take ImageSharp 3
/// and its licence, find another way to draw the image, or accept the risk explicitly.
/// </para>
/// </remarks>
public class ImageSharpIsUnreachableTests
{
    /// <summary>
    /// APIs that would make an ImageSharp decoder reachable. XImage is PdfSharpCore's image
    /// type and is the gateway to all of it.
    /// </summary>
    private static readonly string[] ForbiddenApis =
    [
        "XImage",
        "DrawImage",
        "SixLabors",
        "ImageSharp",
    ];

    [Fact]
    public void The_renderer_draws_no_images_so_the_suppressed_advisories_stay_unreachable()
    {
        var project = ProjectDirectory();

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
        {
            // Skip build output, which contains generated assembly attributes.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var source = StripComments(File.ReadAllText(file));

            foreach (var api in ForbiddenApis)
            {
                if (source.Contains(api, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} uses {api}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "MsAds.Auditor.Reporting now reaches ImageSharp: "
            + string.Join("; ", offenders)
            + ". The NuGetAuditSuppress entries in the project files are justified on the grounds "
            + "that no image API is used, and that is no longer true. Re-evaluate the "
            + "suppressions before proceeding — see the comment in "
            + "MsAds.Auditor.Reporting.csproj.");
    }

    /// <summary>
    /// Removes comments before searching, so the explanatory comments about ImageSharp in the
    /// renderer do not themselves trip the check.
    /// </summary>
    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        source = Regex.Replace(source, @"//[^\n]*", string.Empty);

        return source;
    }

    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MsAds.Auditor.Reporting");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find src/MsAds.Auditor.Reporting by walking up from " + AppContext.BaseDirectory);
    }
}
