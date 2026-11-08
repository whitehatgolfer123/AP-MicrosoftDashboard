using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MsAds.Auditor.Data.Migrations;

/// <summary>
/// Builds a context for <c>dotnet ef</c> without starting the web application.
/// </summary>
/// <remarks>
/// <para>
/// Migrations are provider-specific, and this project supports two, so the provider is chosen by
/// an argument rather than guessed:
/// </para>
/// <code>
/// dotnet ef migrations add &lt;Name&gt; --project src/MsAds.Auditor.Data \
///     --context AuditorDbContext --output-dir Migrations/Sqlite -- --provider sqlite
/// </code>
/// <para>
/// The connection string is a placeholder. Adding or scripting a migration only needs the model
/// and the provider's SQL generator, never a reachable database, so no real credential is
/// involved and none should be put here.
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditorDbContext>
{
    public AuditorDbContext CreateDbContext(string[] args)
    {
        var provider = ProviderFrom(args);
        var builder = new DbContextOptionsBuilder<AuditorDbContext>();

        switch (provider)
        {
            case "postgres" or "postgresql" or "npgsql":
                builder.UseNpgsql(
                    "Host=localhost;Database=msads_auditor_design_time;Username=design;Password=design",
                    npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"));
                break;

            case "sqlite":
                builder.UseSqlite("Data Source=design-time.db");
                break;

            default:
                throw new ArgumentException(
                    $"Unknown provider '{provider}'. Pass `-- --provider sqlite` or "
                    + "`-- --provider postgres`.", nameof(args));
        }

        return new AuditorDbContext(builder.Options);
    }

    /// <summary>
    /// Reads <c>--provider &lt;name&gt;</c> from the arguments after <c>--</c>. Defaults to
    /// SQLite, which is the application's own default provider.
    /// </summary>
    private static string ProviderFrom(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--provider", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1].ToLowerInvariant();
            }
        }

        return "sqlite";
    }
}
