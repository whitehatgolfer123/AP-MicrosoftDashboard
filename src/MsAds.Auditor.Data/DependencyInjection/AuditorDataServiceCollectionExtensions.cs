using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MsAds.Auditor.Core.Changes;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Data.Stores;

namespace MsAds.Auditor.Data.DependencyInjection;

public static class AuditorDataServiceCollectionExtensions
{
    /// <summary>The SQLite file name, created under the user's local application data.</summary>
    public const string SqliteFileName = "msads-auditor.db";

    /// <summary>
    /// Registers the durable stores for the change log, settings and scan history.
    /// </summary>
    /// <remarks>
    /// When the provider is <see cref="PersistenceProvider.None"/> the in-memory and null
    /// implementations are registered instead, so the application runs identically with nothing
    /// written to disk. Registrations use TryAdd so a host that has already chosen its own
    /// implementation — the tests, for instance — keeps it.
    /// </remarks>
    public static IServiceCollection AddAuditorData(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration
            .GetSection(PersistenceOptions.SectionName)
            .Get<PersistenceOptions>() ?? new PersistenceOptions();

        services.TryAddSingleton(TimeProvider.System);

        if (!options.IsEnabled)
        {
            services.TryAddSingleton<IChangeLog>(new InMemoryChangeLog());
            services.TryAddSingleton<ISettingsRepository, NullSettingsRepository>();
            services.TryAddSingleton<IScanHistory, NullScanHistory>();

            return services;
        }

        // A factory rather than a scoped context: the stores are singletons called from page
        // handlers and from background work, and each call needs its own short-lived context.
        services.AddDbContextFactory<AuditorDbContext>(builder =>
            Configure(builder, options));

        services.TryAddSingleton<IChangeLog, EfChangeLog>();
        services.TryAddSingleton<ISettingsRepository, EfSettingsRepository>();
        services.TryAddSingleton<IScanHistory, EfScanHistory>();

        return services;
    }

    private static void Configure(DbContextOptionsBuilder builder, PersistenceOptions options)
    {
        switch (options.Provider)
        {
            case PersistenceProvider.Sqlite:
                builder.UseSqlite(
                    options.ConnectionString ?? DefaultSqliteConnectionString(),
                    sqlite => sqlite.MigrationsAssembly(typeof(AuditorDbContext).Assembly.FullName));
                break;

            case PersistenceProvider.Postgres:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new InvalidOperationException(
                        $"{PersistenceOptions.SectionName}:Provider is Postgres but "
                        + $"{PersistenceOptions.SectionName}:ConnectionString is not set. Supply it "
                        + "with user secrets or an environment variable — never in source control.");
                }

                // The migrations in this assembly are generated for SQLite and their SQL is not
                // portable, so a PostgreSQL deployment cannot share them. Creating the schema
                // from the model instead would leave the database outside migration control,
                // which is worse than refusing: it would work until the first schema change and
                // then have no upgrade path. PostgreSQL is a Phase 27 (hosted deployment) task
                // and needs its own migrations assembly; the README records the command.
                throw new NotSupportedException(
                    "PostgreSQL is configured but not yet supported. Its migrations have not been "
                    + "generated, because there is no PostgreSQL instance to verify them against. "
                    + $"Set {PersistenceOptions.SectionName}:Provider to Sqlite (the default), which "
                    + "needs no server and stores one file. See the PostgreSQL note in README.md.");

            case PersistenceProvider.None:
            default:
                throw new InvalidOperationException(
                    $"Persistence provider {options.Provider} has no configuration.");
        }
    }

    /// <summary>
    /// The default SQLite location: under the user's local application data, never inside the
    /// repository. The change log names client accounts, so it must not end up somewhere it
    /// could be committed.
    /// </summary>
    public static string DefaultSqliteFilePath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        // LocalApplicationData can come back empty on a container with no HOME set, in which
        // case fall back to the current directory rather than writing to the filesystem root.
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Directory.GetCurrentDirectory();
        }

        var directory = Path.Combine(root, "MsAdsAuditor");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, SqliteFileName);
    }

    private static string DefaultSqliteConnectionString()
        => $"Data Source={DefaultSqliteFilePath()}";
}
