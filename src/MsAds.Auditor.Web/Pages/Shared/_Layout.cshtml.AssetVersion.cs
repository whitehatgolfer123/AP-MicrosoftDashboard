namespace MsAds.Auditor.Web.Pages.Shared;

/// <summary>
/// A per-build value appended to static asset URLs so a browser picks up CSS and script changes.
/// Used in place of the <c>asp-append-version</c> tag helper attribute, which mis-compiles in
/// this SDK's Razor generator once a code block appears earlier in the file.
/// </summary>
public static class AssetVersionProvider
{
    /// <summary>Stable for the lifetime of the process, which is what cache busting needs.</summary>
    public static string Current { get; } =
        typeof(AssetVersionProvider).Assembly.GetName().Version?.ToString()
        ?? DateTime.UtcNow.Ticks.ToString();
}
