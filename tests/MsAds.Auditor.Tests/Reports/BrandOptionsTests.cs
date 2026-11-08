using System.Text.RegularExpressions;
using MsAds.Auditor.Core.Reports;

namespace MsAds.Auditor.Tests.Reports;

/// <summary>
/// Keeps the artefact brand values and the stylesheet from drifting apart.
/// </summary>
/// <remarks>
/// A PDF cannot read CSS, so the brand palette is necessarily stated twice: once in the
/// <c>:root</c> block of <c>site.css</c> for the interface, and once in <see cref="BrandOptions"/>
/// for the downloadable artefacts. Duplication drifts, so this test reads the stylesheet and
/// fails if the two disagree. Changing a brand colour in one place without the other breaks the
/// build rather than quietly producing a PDF in the old colours.
/// </remarks>
public class BrandOptionsTests
{
    [Fact]
    public void Every_brand_colour_matches_the_stylesheet()
    {
        var variables = ReadRootVariables();
        var brand = new BrandOptions();

        Assert.NotEmpty(variables);

        foreach (var (cssVariable, brandValue) in brand.CssVariableMap)
        {
            Assert.True(
                variables.TryGetValue(cssVariable, out var cssValue),
                $"site.css no longer defines {cssVariable}, which BrandOptions mirrors. Either "
                + "restore the custom property or remove it from BrandOptions.CssVariableMap.");

            Assert.True(
                string.Equals(cssValue, brandValue, StringComparison.OrdinalIgnoreCase),
                $"{cssVariable} is {cssValue} in site.css but {brandValue} in BrandOptions. "
                + "The downloadable artefacts would be rendered in a different colour from the "
                + "interface. Update whichever is out of date.");
        }
    }

    [Fact]
    public void The_mapping_covers_every_colour_the_artefacts_use()
    {
        var brand = new BrandOptions();

        // Guards against a colour being added to BrandOptions and left out of the drift check,
        // which would reintroduce exactly the silent divergence this test exists to prevent.
        var mapped = brand.CssVariableMap.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var colourProperties = typeof(BrandOptions)
            .GetProperties()
            .Where(p => p.Name.EndsWith("Hex", StringComparison.Ordinal))
            .Select(p => (Name: p.Name, Value: (string?)p.GetValue(brand)))
            .ToList();

        Assert.NotEmpty(colourProperties);

        foreach (var (name, value) in colourProperties)
        {
            Assert.True(
                value is not null && mapped.Contains(value),
                $"BrandOptions.{name} is not covered by CssVariableMap, so nothing checks it "
                + "against site.css. Add it to the map.");
        }
    }

    [Theory]
    [InlineData("#1769AA")]
    [InlineData("#123B5D")]
    public void Colours_are_six_digit_hex_so_the_renderer_can_parse_them(string expected)
    {
        var brand = new BrandOptions();

        Assert.Contains(expected, brand.CssVariableMap.Values);

        foreach (var value in brand.CssVariableMap.Values)
        {
            Assert.Matches("^#[0-9A-Fa-f]{6}$", value);
        }
    }

    /// <summary>
    /// Parses the custom properties out of the single <c>:root</c> block in site.css.
    /// </summary>
    private static Dictionary<string, string> ReadRootVariables()
    {
        var css = File.ReadAllText(StylesheetPath());

        var rootStart = css.IndexOf(":root", StringComparison.Ordinal);
        Assert.True(rootStart >= 0, "site.css no longer has a :root block.");

        var open = css.IndexOf('{', rootStart);
        var close = css.IndexOf('}', open);
        Assert.True(open > 0 && close > open, "The :root block in site.css is malformed.");

        var block = css[(open + 1)..close];

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Only literal hex values: the derived shades reference other variables with var(), and
        // those are the stylesheet's own business.
        foreach (var match in Regex
            .Matches(block, @"(--[a-z0-9-]+)\s*:\s*(#[0-9A-Fa-f]{3,8})\s*;", RegexOptions.IgnoreCase)
            .Cast<Match>())
        {
            variables[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return variables;
    }

    /// <summary>
    /// Walks up from the test assembly to the repository, the same approach the existing Razor
    /// construct tests use.
    /// </summary>
    private static string StylesheetPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "MsAds.Auditor.Web", "wwwroot", "css", "site.css");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not find src/MsAds.Auditor.Web/wwwroot/css/site.css by walking up from "
            + AppContext.BaseDirectory);
    }
}
