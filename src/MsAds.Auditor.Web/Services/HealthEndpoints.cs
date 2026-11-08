using Microsoft.EntityFrameworkCore;
using MsAds.Auditor.Data;

namespace MsAds.Auditor.Web.Services;

/// <summary>
/// Liveness and readiness endpoints, for a container orchestrator or a monitor.
/// </summary>
/// <remarks>
/// <para>
/// Two endpoints rather than one, because the questions differ. <c>/health/live</c> asks whether
/// the process is up, and must not touch a dependency: a database blip should not cause an
/// orchestrator to restart a perfectly healthy process. <c>/health/ready</c> asks whether the
/// application can actually do its job, which here means the change log is writable — writes to
/// advertising accounts must not proceed if they cannot be recorded.
/// </para>
/// <para>
/// Both are deliberately anonymous and deliberately terse. They are reachable without sign-in,
/// so they reveal nothing beyond up/down: no versions, no configuration, no account names.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Text("OK"))
            .AllowAnonymous()
            .WithName("Liveness");

        app.MapGet("/health/ready", async (
            IServiceProvider services, CancellationToken cancellationToken) =>
        {
            var options = services.GetService<Microsoft.Extensions.Options.IOptions<PersistenceOptions>>();

            // With persistence switched off there is no dependency to be ready for.
            if (options?.Value.IsEnabled != true)
            {
                return Results.Text("OK");
            }

            var factory = services.GetService<IDbContextFactory<AuditorDbContext>>();

            if (factory is null)
            {
                return Results.Text("OK");
            }

            try
            {
                await using var db = await factory
                    .CreateDbContextAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    return Results.Text("NOT READY", statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Text("OK");
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The reason is logged by the framework; it is not returned, because this
                // endpoint is anonymous and a connection string should never reach it.
                return Results.Text("NOT READY", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
            .AllowAnonymous()
            .WithName("Readiness");
    }
}
