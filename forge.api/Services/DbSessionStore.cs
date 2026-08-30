using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Database-backed session registry (user_sessions), replacing the retired
/// in-memory dictionary: sessions survive API restarts, revocation is
/// durable, and every session carries auth method, IP, and user agent.
/// Validation runs on every authenticated request, so positive lookups are
/// memory-cached briefly (<see cref="ValidationCacheTtl"/>); a revocation
/// therefore takes effect within that window at worst, immediately on this
/// instance.
/// </summary>
public class DbSessionStore(
    AppDbContext db,
    IMemoryCache cache,
    IClock clock,
    ILogger<DbSessionStore> logger) : ISessionStore
{
    private static readonly TimeSpan ValidationCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PruneRetention = TimeSpan.FromDays(7);

    private static string CacheKey(string jti) => $"session:{jti}";

    public async Task CreateSessionAsync(int userId, string jti, DateTimeOffset expiresAt,
        string? authMethod = null, string? ipAddress = null, string? userAgent = null,
        CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        db.UserSessions.Add(new UserSession
        {
            UserId = userId,
            Jti = jti,
            AuthMethod = authMethod,
            IpAddress = ipAddress,
            UserAgent = Truncate(userAgent, 500),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(ct);

        // Opportunistic prune: long-expired rows ride out on login traffic
        // instead of needing a dedicated job.
        await db.UserSessions
            .Where(s => s.ExpiresAt < now - PruneRetention)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<bool> ValidateSessionAsync(string jti, CancellationToken ct = default)
    {
        if (cache.TryGetValue<bool>(CacheKey(jti), out var cachedValid))
            return cachedValid;

        var now = clock.UtcNow;
        var session = await db.UserSessions.AsNoTracking()
            .Where(s => s.Jti == jti)
            .Select(s => new { s.ExpiresAt, s.RevokedAt })
            .FirstOrDefaultAsync(ct);

        var valid = session is not null && session.RevokedAt is null && session.ExpiresAt > now;

        if (valid)
        {
            var ttl = session!.ExpiresAt - now < ValidationCacheTtl
                ? session.ExpiresAt - now
                : ValidationCacheTtl;
            cache.Set(CacheKey(jti), true, ttl);
        }

        return valid;
    }

    public async Task RevokeSessionAsync(string jti, CancellationToken ct = default)
    {
        await db.UserSessions
            .Where(s => s.Jti == jti && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.UtcNow), ct);

        cache.Remove(CacheKey(jti));
    }

    public async Task RevokeAllUserSessionsAsync(int userId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var jtis = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .Select(s => s.Jti)
            .ToListAsync(ct);

        var count = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);

        foreach (var jti in jtis)
            cache.Remove(CacheKey(jti));

        logger.LogInformation("Revoked {Count} sessions for user {UserId}", count, userId);
    }

    public async Task<string?> UpdateSessionJtiAsync(string oldJti, string newJti,
        DateTimeOffset newExpiresAt, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var updated = await db.UserSessions
            .Where(s => s.Jti == oldJti && s.RevokedAt == null && s.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Jti, newJti)
                .SetProperty(x => x.ExpiresAt, newExpiresAt)
                .SetProperty(x => x.LastRefreshedAt, now), ct);

        cache.Remove(CacheKey(oldJti));

        return updated > 0 ? newJti : null;
    }

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
