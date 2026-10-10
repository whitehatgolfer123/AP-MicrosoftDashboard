using System.Text.RegularExpressions;

namespace MsAds.Auditor.Tests.Web;

/// <summary>
/// Guards against the Razor constructs that .NET SDK 10.0.401 mis-compiles.
/// </summary>
/// <remarks>
/// Once a page contains a code block, this SDK mis-maps the source span of any tag helper
/// attribute that takes a non-string value, and of <c>&lt;text&gt;</c> blocks. The generated C#
/// is then garbled, with errors reported far from the real cause — so these are expensive to
/// diagnose and cheap to prevent. Reproduced on a stock <c>dotnet new webapp</c>, so it is the
/// SDK rather than anything specific to this project.
/// </remarks>
public class RazorConstructTests
{
    private static readonly string PagesRoot = LocatePages();

    public static TheoryData<string> RazorFiles()
    {
        var data = new TheoryData<string>();

        foreach (var file in Directory.EnumerateFiles(PagesRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            data.Add(file);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RazorFiles))]
    public void No_view_uses_a_construct_this_sdk_miscompiles(string file)
    {
        var markup = File.ReadAllText(file);
        var name = Path.GetFileName(file);

        Assert.DoesNotContain("<text>", markup);
        Assert.DoesNotContain("asp-for=", markup);
        Assert.DoesNotContain("asp-items=", markup);
        Assert.DoesNotContain("asp-all-route-data=", markup);
        Assert.DoesNotContain("asp-append-version=", markup);

        // The partial tag helper breaks inside a code block; Html.PartialAsync does not.
        Assert.False(
            markup.Contains("<partial ", StringComparison.Ordinal),
            $"{name} uses the <partial> tag helper; use @await Html.PartialAsync(...) instead.");

        // A single-line @if with markup is rejected outright by the Razor parser here.
        Assert.False(
            Regex.IsMatch(markup, @"@if \(.*\) \{ *<"),
            $"{name} has a single-line @if containing markup; spread it over multiple lines.");

        // An implicit expression cannot begin with a C# keyword.
        Assert.False(
            Regex.IsMatch(markup, @"@(string|int|long|decimal|double|bool|object|new|typeof|default)\."),
            $"{name} starts an implicit expression with a keyword; wrap it as @(...).");
    }

    /// <summary>Walks up to the repository root to find the web project's Pages folder.</summary>
    private static string LocatePages()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MsAds.Auditor.Web", "Pages");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the web project's Pages folder.");
    }
}
