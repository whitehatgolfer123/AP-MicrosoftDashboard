using Microsoft.Extensions.Options;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Data.Stores;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Tests.Persistence;

/// <summary>
/// The saved analysis thresholds, against a real SQLite database.
/// </summary>
public class SettingsPersistenceTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 11, 8, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Nothing_is_stored_until_something_is_saved()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        // Null rather than a row of defaults, because "nothing saved" has to stay
        // distinguishable from "saved, and happens to match the defaults" — the first means
        // configuration is still in charge.
        Assert.Null(await NewRepository(database).LoadAsync());
    }

    [Fact]
    public async Task Saved_thresholds_survive_a_restart()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var settings = new AuditSettings
        {
            TargetCpa = 42.50m,
            TargetRoas = 3.75m,
            MinimumSpend = 125m,
            MinimumClicks = 35,
            MinimumConversions = 7.5m,
            CpaToleranceAboveTargetPercent = 15m,
            SignificantChangePercent = 25m,
            CtrDeclinePercent = 18m,
            SearchTermMinimumSpend = 30m,
            SearchTermMinimumClicks = 12,
            CurrencyCode = "EUR",
            CultureName = "de-DE",
            MinimumImpressionsForCtr = 750,
            ImpressionShareLostToBudgetPercent = 22m,
            DeliveryDropPercent = 65m,
            CpaVersusAccountMultiple = 1.8m,
            ChangeImpactWindowDays = 5,
        };

        await NewRepository(database).SaveAsync(settings, "tom@addpeople.co.uk");

        var loaded = await NewRepository(database).LoadAsync();

        // Every field, because a mapping that silently drops one would reset a threshold to its
        // default on the next restart without anyone noticing.
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public async Task Saving_twice_updates_the_single_row_rather_than_adding_another()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var repository = NewRepository(database);

        await repository.SaveAsync(new AuditSettings { MinimumSpend = 100m });
        await repository.SaveAsync(new AuditSettings { MinimumSpend = 200m });

        var loaded = await repository.LoadAsync();

        Assert.Equal(200m, loaded!.MinimumSpend);

        // The check constraint pins the key, so a second row could not exist; this asserts the
        // store updates rather than relying on the constraint to reject an insert.
        await using var db = database.Factory.CreateDbContext();
        Assert.Equal(1, db.Settings.Count());
    }

    [Fact]
    public async Task Clearing_forgets_the_row_so_configuration_applies_again()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var repository = NewRepository(database);

        await repository.SaveAsync(new AuditSettings { MinimumSpend = 999m });
        await repository.ClearAsync();

        // Deleted rather than overwritten with today's defaults, so a later change to
        // configuration is picked up instead of being frozen at the moment of the reset.
        Assert.Null(await repository.LoadAsync());
    }

    [Fact]
    public async Task The_store_reports_whether_it_loaded_saved_values()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var configured = new AuditSettings { MinimumSpend = 50m };
        var store = NewStore(database, configured);

        await store.LoadAsync();

        Assert.False(store.IsPersisted);
        Assert.Equal(configured, store.Current);

        await store.UpdateAsync(new AuditSettings { MinimumSpend = 321m }, "tom@addpeople.co.uk");

        Assert.True(store.IsPersisted);
        Assert.Equal(321m, store.Current.MinimumSpend);

        // A fresh store over the same database is what a restart looks like.
        var restarted = NewStore(database, configured);
        await restarted.LoadAsync();

        Assert.True(restarted.IsPersisted);
        Assert.Equal(321m, restarted.Current.MinimumSpend);
    }

    [Fact]
    public async Task Resetting_returns_the_store_to_the_configured_defaults()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        var configured = new AuditSettings { MinimumSpend = 50m };
        var store = NewStore(database, configured);

        await store.UpdateAsync(new AuditSettings { MinimumSpend = 321m });
        await store.ResetToDefaultsAsync();

        Assert.False(store.IsPersisted);
        Assert.Equal(configured, store.Current);

        var restarted = NewStore(database, configured);
        await restarted.LoadAsync();

        Assert.Equal(configured, restarted.Current);
    }

    [Fact]
    public async Task A_failed_save_leaves_the_working_copy_alone()
    {
        var configured = new AuditSettings { MinimumSpend = 50m };
        var store = new PersistentSettingsStore(
            new OptionsWrapper<AuditorOptions>(new AuditorOptions { Defaults = configured }),
            new FailingSettingsRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateAsync(new AuditSettings { MinimumSpend = 321m }));

        // Write-through, not write-behind: if the row did not save, the screen must not go on
        // showing a value that will vanish at the next restart.
        Assert.Equal(configured, store.Current);
        Assert.False(store.IsPersisted);
    }

    private static EfSettingsRepository NewRepository(SqliteDatabase database)
        => new(database.Factory, TimeProvider.System);

    private static PersistentSettingsStore NewStore(SqliteDatabase database, AuditSettings defaults)
        => new(
            new OptionsWrapper<AuditorOptions>(new AuditorOptions { Defaults = defaults }),
            NewRepository(database));

    /// <summary>Stands in for a database that cannot be written to.</summary>
    private sealed class FailingSettingsRepository : ISettingsRepository
    {
        public Task<AuditSettings?> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<AuditSettings?>(null);

        public Task SaveAsync(
            AuditSettings settings, string? updatedBy = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The database is unavailable.");

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
