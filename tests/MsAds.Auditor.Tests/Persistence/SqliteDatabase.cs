using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsAds.Auditor.Data;

namespace MsAds.Auditor.Tests.Persistence;

/// <summary>
/// A throwaway SQLite database for one test, created by running the real migrations.
/// </summary>
/// <remarks>
/// A file rather than <c>:memory:</c> on purpose. The migrations are the thing being tested as
/// much as the stores are, and an in-memory database would let a test pass against a schema
/// built by the model instead of the migration that will actually run in production. The file is
/// deleted on dispose.
/// </remarks>
internal sealed class SqliteDatabase : IAsyncDisposable
{
    private readonly string _path;

    private SqliteDatabase(string path, IDbContextFactory<AuditorDbContext> factory)
    {
        _path = path;
        Factory = factory;
    }

    internal IDbContextFactory<AuditorDbContext> Factory { get; }

    internal static async Task<SqliteDatabase> CreateAsync()
    {
        var path = Path.Combine(
            Path.GetTempPath(), $"msads-auditor-test-{Guid.NewGuid():N}.db");

        IDbContextFactory<AuditorDbContext> factory =
            new TestDbContextFactory($"Data Source={path}");

        await using var db = factory.CreateDbContext();

        // The real migrations, not EnsureCreated: the migration is as much under test as the
        // stores are.
        await db.Database.MigrateAsync();

        return new SqliteDatabase(path, factory);
    }

    /// <summary>The persistence options the stores under test read, with trimming switched off.</summary>
    internal static IOptions<PersistenceOptions> Options(
        int changeLogLimit = 0, int scanHistoryLimit = 500)
        => new OptionsWrapper<PersistenceOptions>(new PersistenceOptions
        {
            Provider = PersistenceProvider.Sqlite,
            ChangeLogLimit = changeLogLimit,
            ScanHistoryLimit = scanHistoryLimit,
        });

    internal static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;

    public async ValueTask DisposeAsync()
    {
        // SQLite holds the file open through a connection pool, which keeps a Windows delete
        // from succeeding until it is cleared.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        await Task.Yield();

        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private sealed class TestDbContextFactory(string connectionString)
        : IDbContextFactory<AuditorDbContext>
    {
        public AuditorDbContext CreateDbContext()
        {
            var builder = new DbContextOptionsBuilder<AuditorDbContext>()
                .UseSqlite(connectionString);

            return new AuditorDbContext(builder.Options);
        }
    }
}
