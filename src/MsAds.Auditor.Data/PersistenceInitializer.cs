using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MsAds.Auditor.Data;

/// <summary>
/// Brings the database up to date at startup, and reports what it did.
/// </summary>
/// <remarks>
/// Called explicitly from the host rather than registered as a hosted service, so that a failure
/// stops the application starting instead of surfacing later as an error on the first write. If
/// the change log cannot be written to, the operator needs to know before anyone applies a
/// change, not afterwards.
/// </remarks>
public static class PersistenceInitializer
{
    public static async Task InitialiseAsync(
        IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<PersistenceOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PersistenceInitializer));

        if (!options.IsEnabled)
        {
            logger.LogWarning(
                "Persistence is switched off ({Section}:Provider = None). The change log, settings "
                + "and scan history are in memory only and a restart will clear them.",
                PersistenceOptions.SectionName);
            return;
        }

        var factory = services.GetRequiredService<IDbContextFactory<AuditorDbContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (!options.MigrateOnStartup)
        {
            var outstanding = await db.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);

            var pending = outstanding.ToList();

            if (pending.Count > 0)
            {
                // Not fatal: the operator asked to control migrations themselves, so say what is
                // outstanding and let them decide.
                logger.LogError(
                    "{Count} database migration(s) have not been applied and "
                    + "{Section}:MigrateOnStartup is false: {Migrations}. Apply them with "
                    + "`dotnet ef database update`.",
                    pending.Count, PersistenceOptions.SectionName, string.Join(", ", pending));
            }

            return;
        }

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Persistence ready on {Provider}. The change log, settings and scan verdicts are "
            + "stored; client performance data is not.",
            options.Provider);
    }
}
