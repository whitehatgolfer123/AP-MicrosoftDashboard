using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MsAds.Auditor.Data.Entities;

namespace MsAds.Auditor.Data;

/// <summary>
/// The durable records: the change log, the saved settings and the portfolio scan history.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrow. Client performance data — reports, metrics, campaign, keyword and search
/// term rows — is never stored. That was a privacy decision taken before persistence existed and
/// it has not been reversed: those stores remain in memory and a restart clears them. What is
/// here is the record of what the tool changed, the operator's thresholds, and scan verdicts
/// without the figures behind them.
/// </para>
/// <para>
/// Provider-neutral: the same model runs on SQLite and PostgreSQL. Anything a provider renders
/// differently is normalised here rather than in a provider-specific migration — durations as
/// integer milliseconds, string collections as JSON, enums as strings.
/// </para>
/// </remarks>
public sealed class AuditorDbContext(DbContextOptions<AuditorDbContext> options)
    : DbContext(options)
{
    internal DbSet<ChangeRunEntity> ChangeRuns => Set<ChangeRunEntity>();

    internal DbSet<ChangeOutcomeEntity> ChangeOutcomes => Set<ChangeOutcomeEntity>();

    internal DbSet<ScanRunEntity> ScanRuns => Set<ScanRunEntity>();

    internal DbSet<ScanVerdictEntity> ScanVerdicts => Set<ScanVerdictEntity>();

    internal DbSet<StoredSettingsEntity> Settings => Set<StoredSettingsEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureChangeLog(builder);
        ConfigureScanHistory(builder);
        ConfigureSettings(builder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        // Money and thresholds. 19,4 is wide enough for any account's lifetime spend and keeps
        // four decimal places for ROAS and conversion fractions. Left to the provider default a
        // decimal silently becomes REAL on SQLite, which would round currency.
        builder.Properties<decimal>().HavePrecision(19, 4);
        builder.Properties<decimal?>().HavePrecision(19, 4);

        // Identifiers and short labels get a sensible ceiling rather than unbounded text.
        builder.Properties<string>().HaveMaxLength(512);
    }

    private static void ConfigureChangeLog(ModelBuilder builder)
    {
        builder.Entity<ChangeRunEntity>(run =>
        {
            run.ToTable("change_runs");
            run.HasKey(e => e.RunId);

            // The change log is read newest-first and filtered by account; both are indexed
            // because this table only ever grows.
            run.HasIndex(e => e.RanAt).IsDescending();
            run.HasIndex(e => new { e.AccountId, e.WasPreview });

            run.Property(e => e.RanAt).HasConversion(UtcTicksConverter);
            run.Property(e => e.AccountName).HasMaxLength(256).IsRequired();
            run.Property(e => e.RanBy).HasMaxLength(256);

            run.HasMany(e => e.Outcomes)
                .WithOne(o => o.Run!)
                .HasForeignKey(o => o.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChangeOutcomeEntity>(outcome =>
        {
            outcome.ToTable("change_outcomes");
            outcome.HasKey(e => e.Id);

            // AppliedChangeIds filters on exactly this combination.
            outcome.HasIndex(e => new { e.ChangeAccountId, e.Status });
            outcome.HasIndex(e => new { e.RunId, e.Ordinal });

            // Enums as text. An audit trail has to stay readable years later, and a column of
            // integers would need this assembly to interpret.
            outcome.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            outcome.Property(e => e.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
            outcome.Property(e => e.Risk).HasConversion<string>().HasMaxLength(32).IsRequired();

            outcome.Property(e => e.ChangeId).HasMaxLength(256).IsRequired();
            outcome.Property(e => e.TargetDescription).HasMaxLength(512).IsRequired();
            outcome.Property(e => e.Value).HasMaxLength(1024);
            outcome.Property(e => e.MatchType).HasMaxLength(32);
            outcome.Property(e => e.Source).HasMaxLength(128).IsRequired();

            // Prose fields: these carry the reason a change was made, which is the point of the
            // audit trail, so they are not truncated to the 512 default.
            outcome.Property(e => e.Message).HasMaxLength(2048).IsRequired();
            outcome.Property(e => e.Summary).HasMaxLength(2048).IsRequired();
            outcome.Property(e => e.Rationale).HasMaxLength(4096).IsRequired();

            outcome.Property(e => e.Caveats)
                .HasConversion(StringListConverter)
                .Metadata.SetValueComparer(StringListComparer);
        });
    }

    private static void ConfigureScanHistory(ModelBuilder builder)
    {
        builder.Entity<ScanRunEntity>(run =>
        {
            run.ToTable("scan_runs");
            run.HasKey(e => e.RunId);
            run.HasIndex(e => e.GeneratedAt).IsDescending();

            run.Property(e => e.GeneratedAt).HasConversion(UtcTicksConverter);
            run.Property(e => e.Trigger).HasConversion<string>().HasMaxLength(32).IsRequired();
            run.Property(e => e.FailureMessage).HasMaxLength(2048);

            run.HasMany(e => e.Accounts)
                .WithOne(a => a.Run!)
                .HasForeignKey(a => a.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ScanVerdictEntity>(verdict =>
        {
            verdict.ToTable("scan_verdicts");
            verdict.HasKey(e => e.Id);

            // One row per account per run, which also stops a retry duplicating verdicts.
            verdict.HasIndex(e => new { e.RunId, e.AccountId }).IsUnique();

            // VerdictsBeforeAsync walks back through an account's verdicts by time.
            verdict.HasIndex(e => e.AccountId);

            verdict.Property(e => e.Verdict).HasConversion<string>().HasMaxLength(32).IsRequired();
            verdict.Property(e => e.AccountName).HasMaxLength(256).IsRequired();
        });
    }

    private static void ConfigureSettings(ModelBuilder builder)
    {
        builder.Entity<StoredSettingsEntity>(settings =>
        {
            settings.ToTable("settings");
            settings.HasKey(e => e.Id);

            // Never generated: the single row is written with the fixed singleton key, so an
            // insert of a second row is a constraint violation rather than a silent duplicate.
            settings.Property(e => e.Id).ValueGeneratedNever();

            settings.Property(e => e.UpdatedAt).HasConversion(UtcTicksConverter);
            settings.Property(e => e.CurrencyCode).HasMaxLength(8).IsRequired();
            settings.Property(e => e.CultureName).HasMaxLength(32).IsRequired();
            settings.Property(e => e.UpdatedBy).HasMaxLength(256);

            settings.ToTable(t => t.HasCheckConstraint(
                "ck_settings_singleton", $"\"Id\" = {StoredSettingsEntity.SingletonId}"));
        });
    }

    /// <summary>
    /// Stores an instant as UTC ticks.
    /// </summary>
    /// <remarks>
    /// SQLite refuses to order by a <see cref="DateTimeOffset"/> at all: its default mapping is
    /// text carrying the offset, which does not sort correctly once two rows have different
    /// offsets, so EF blocks the comparison rather than return the wrong order. Both the change
    /// log and the scan history are read newest-first, so that ordering is not optional. Ticks
    /// sort numerically, are exact, and mean the same thing on both providers.
    ///
    /// The original offset is not preserved, and nothing needs it: every timestamp written here
    /// comes from the clock as UTC. Reading back therefore yields the same instant with a zero
    /// offset.
    /// </remarks>
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter =
        new(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    /// <summary>
    /// Stores a string list as a JSON array. SQLite has no array type and PostgreSQL's native
    /// one would make the model provider-specific, so JSON keeps a single shape for both.
    /// </summary>
    private static readonly ValueConverter<List<string>, string> StringListConverter =
        new(
            value => JsonSerializer.Serialize(value, JsonOptions),
            text => JsonSerializer.Deserialize<List<string>>(text, JsonOptions) ?? new List<string>());

    /// <summary>
    /// Without this EF compares the list by reference and never notices an in-place edit, so a
    /// changed caveat would not be saved.
    /// </summary>
    private static readonly ValueComparer<List<string>> StringListComparer =
        new(
            (left, right) => left!.SequenceEqual(right!, StringComparer.Ordinal),
            list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
            list => list.ToList());

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
}
