using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Forge.Identity.Interfaces;

/// <summary>
/// Phase C — the Identity vertical's segregated view of the application
/// DbContext. Exposes ONLY the Identity-owned <see cref="DbSet{TEntity}"/>s
/// plus the handful of <c>DbContext</c> members Identity handlers use, so
/// those handlers can depend on this instead of the concrete
/// <c>Forge.Data.Context.AppDbContext</c>.
///
/// <para>
/// Why this exists: a single fat <c>IAppDbContext</c> exposing every
/// <c>DbSet</c> would have to reference every vertical's entity types,
/// inverting the dependency graph. A per-vertical interface keeps the edge
/// one-way — <c>Forge.Identity</c> references only its own entities, and the
/// concrete <c>AppDbContext</c> (which already has every <c>DbSet</c>)
/// implements all the vertical interfaces "for free." Handlers bind the
/// interface; DI resolves it to the single <c>AppDbContext</c> scope.
/// </para>
///
/// <para>
/// This is the prerequisite that lets Identity's MediatR handlers move into
/// <c>Forge.Identity</c> later without a <c>Forge.Identity → forge.data</c>
/// cycle. See <c>TODO.md</c> §"Next effort — extract IAppDbContext".
/// </para>
/// </summary>
public interface IIdentityDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<UserPreference> UserPreferences { get; }
    DbSet<UserMfaDevice> UserMfaDevices { get; }
    DbSet<MfaRecoveryCode> MfaRecoveryCodes { get; }
    DbSet<UserScanIdentifier> UserScanIdentifiers { get; }
    DbSet<UserScanDevice> UserScanDevices { get; }
    DbSet<UserIntegration> UserIntegrations { get; }
    DbSet<UserCloudStorageLink> UserCloudStorageLinks { get; }
    DbSet<CloudStorageProvider> CloudStorageProviders { get; }
    DbSet<EntityCloudLink> EntityCloudLinks { get; }
    DbSet<OAuthStateToken> OAuthStateTokens { get; }
    DbSet<KioskTerminal> KioskTerminals { get; }

    /// <summary>Persist tracked changes. Mirrors <c>DbContext.SaveChangesAsync</c>.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Access change-tracking entry for an entity (state manipulation).</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>Database facade — transactions, raw SQL, connection access.</summary>
    DatabaseFacade Database { get; }
}
