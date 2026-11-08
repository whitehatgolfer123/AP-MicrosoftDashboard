using System.ComponentModel.DataAnnotations;

namespace MsAds.Auditor.Data;

/// <summary>Which database the durable records are kept in.</summary>
public enum PersistenceProvider
{
    /// <summary>
    /// Nothing is persisted; every store is in-memory and a restart clears it. This was the
    /// application's only behaviour before persistence existed, and it stays available for a
    /// run where nothing should touch the disk.
    /// </summary>
    None,

    /// <summary>
    /// A single SQLite file. The default: free, no server, nothing to administer, and a backup
    /// is a file copy. Appropriate for the single-machine deployment the agency actually has.
    /// </summary>
    Sqlite,

    /// <summary>
    /// PostgreSQL, for a hosted deployment with managed backup. Supported because ROADMAP.md
    /// targets it, but see the note on <see cref="ConnectionString"/>.
    /// </summary>
    Postgres,
}

/// <summary>
/// Where and whether the change log, settings and scan history are stored.
/// </summary>
public sealed class PersistenceOptions
{
    public const string SectionName = "Auditor:Persistence";

    /// <summary>
    /// SQLite by default, so a fresh checkout persists the change log without any setup. The
    /// change log is the one record that genuinely must survive a restart: it says what this
    /// tool altered in a client's account and who authorised it.
    /// </summary>
    public PersistenceProvider Provider { get; init; } = PersistenceProvider.Sqlite;

    /// <summary>
    /// The connection string. Left unset for SQLite, in which case a file named
    /// <c>msads-auditor.db</c> is created under the user's local application data — outside the
    /// repository, so it can never be committed.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Applies any outstanding migrations at startup. Convenient for SQLite on one machine,
    /// where there is no separate deployment step. Turn it off for a hosted database, where
    /// migrations should be a deliberate release action rather than something a web process does
    /// while starting.
    /// </summary>
    public bool MigrateOnStartup { get; init; } = true;

    /// <summary>
    /// How many apply runs the change log keeps. Zero or less means keep everything, which is
    /// the right answer for an audit trail; a limit exists only for a long-running demo
    /// installation that would otherwise grow without bound.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int ChangeLogLimit { get; init; }

    /// <summary>How many portfolio scans are kept before the oldest are dropped.</summary>
    [Range(1, int.MaxValue)]
    public int ScanHistoryLimit { get; init; } = 500;

    public bool IsEnabled => Provider != PersistenceProvider.None;

    /// <summary>
    /// Settings that are present but unusable, for the startup check to report. A missing
    /// PostgreSQL connection string is a configuration mistake rather than something to guess
    /// at, because silently falling back to a local file would hide it.
    /// </summary>
    public IReadOnlyList<string> MissingSettings()
    {
        var missing = new List<string>();

        if (Provider == PersistenceProvider.Postgres && string.IsNullOrWhiteSpace(ConnectionString))
        {
            missing.Add($"{SectionName}:ConnectionString");
        }

        return missing;
    }
}
