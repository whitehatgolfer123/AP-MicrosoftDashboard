using Microsoft.EntityFrameworkCore;
using MsAds.Auditor.Core.Persistence;
using MsAds.Auditor.Core.Settings;
using MsAds.Auditor.Data.Entities;
using MsAds.Auditor.Data.Mapping;

namespace MsAds.Auditor.Data.Stores;

/// <summary>
/// Stores the analysis thresholds in the single settings row, so values entered in the Settings
/// screen survive a restart instead of reverting to configuration.
/// </summary>
public sealed class EfSettingsRepository(
    IDbContextFactory<AuditorDbContext> contextFactory,
    TimeProvider timeProvider) : ISettingsRepository
{
    public async Task<AuditSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var stored = await db.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == StoredSettingsEntity.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        return stored is null ? null : SettingsMapping.ToDomain(stored);
    }

    public async Task SaveAsync(
        AuditSettings settings, string? updatedBy = null, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await db.Settings
            .FirstOrDefaultAsync(s => s.Id == StoredSettingsEntity.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new StoredSettingsEntity();
            db.Settings.Add(existing);
        }

        SettingsMapping.CopyOnto(existing, settings, timeProvider.GetUtcNow(), updatedBy);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // Deleting the row rather than writing the defaults into it: an absent row means "use
        // configuration", which is what reset-to-defaults should restore. Writing today's
        // defaults would freeze them against a later configuration change.
        await db.Settings
            .Where(s => s.Id == StoredSettingsEntity.SingletonId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
