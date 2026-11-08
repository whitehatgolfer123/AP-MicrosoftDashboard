using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MsAds.Auditor.Core.Reports;

namespace MsAds.Auditor.Reporting.DependencyInjection;

public static class AuditorReportingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the downloadable-artefact renderer and the brand values it draws with.
    /// </summary>
    /// <remarks>
    /// A singleton: the font faces are read from disk once and the resolver PdfSharpCore uses is
    /// process-wide anyway, so a renderer per request would re-read several megabytes of font for
    /// no benefit.
    /// </remarks>
    public static IServiceCollection AddAuditorReporting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<BrandOptions>()
            .Bind(configuration.GetSection(BrandOptions.SectionName));

        services.TryAddSingleton<IReportRenderer, PdfReportRenderer>();

        return services;
    }
}
