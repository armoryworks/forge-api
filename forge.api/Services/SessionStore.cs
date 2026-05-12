using System.Collections.Concurrent;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public class SessionStore(ILogger<SessionStore> logger) : ISessionStore
{
    private record SessionEntry(int UserId, DateTimeOffset ExpiresAt);

    /// <summary>
    /// JTI → session entry. Cleared on container restart, which automatically
    /// invalidates all tokens issued by previous instances.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SessionEntry> Sessions = new();

    /// <summary>
    /// UserId → set of active JTIs. Enables bulk revocation (password change, deactivation).
    /// </summary>
    private static readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> UserJtis = new();

    public Task CreateSessionAsync(int userId, string jti, DateTimeOffset expiresAt,
        string? authMethod = null, string? ipAddress = null, string? userAgent = null,
        CancellationToken ct = default)
    {
        Sessions[jti] = new SessionEntry(userId, expiresAt);

        var userSet = UserJtis.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        userSet[jti] = 0;

        logger.LogDebug("Session created for user {UserId}, jti={Jti}, method={AuthMethod}",
            userId, jti, authMethod);

        return Task.CompletedTask;
    }

    public Task<bool> ValidateSessionAsync(string jti, CancellationToken ct = default)
    {
        if (!Sessions.TryGetValue(jti, out var entry))
            return Task.FromResult(false);

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            // Expired — clean up
            RemoveSession(jti, entry.UserId);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task RevokeSessionAsync(string jti, CancellationToken ct = default)
    {
        if (Sessions.TryRemove(jti, out var entry))
        {
            RemoveFromUserIndex(jti, entry.UserId);
        }

        return Task.CompletedTask;
    }

    public Task RevokeAllUserSessionsAsync(int userId, CancellationToken ct = default)
    {
        if (UserJtis.TryRemove(userId, out var jtis))
        {
            var count = 0;
            foreach (var jti in jtis.Keys)
            {
                Sessions.TryRemove(jti, out _);
                count++;
            }

            logger.LogInformation("Revoked {Count} sessions for user {UserId}", count, userId);
        }

        return Task.CompletedTask;
    }

    public Task<string?> UpdateSessionJtiAsync(string oldJti, string newJti,
        DateTimeOffset newExpiresAt, CancellationToken ct = default)
    {
        if (!Sessions.TryRemove(oldJti, out var entry))
            return Task.FromResult<string?>(null);

        // Replace with new JTI
        Sessions[newJti] = new SessionEntry(entry.UserId, newExpiresAt);

        // Update user index
        RemoveFromUserIndex(oldJti, entry.UserId);
        var userSet = UserJtis.GetOrAdd(entry.UserId, _ => new ConcurrentDictionary<string, byte>());
        userSet[newJti] = 0;

        return Task.FromResult<string?>(newJti);
    }

    private static void RemoveSession(string jti, int userId)
    {
        Sessions.TryRemove(jti, out _);
        RemoveFromUserIndex(jti, userId);
    }

    private static void RemoveFromUserIndex(string jti, int userId)
    {
        if (UserJtis.TryGetValue(userId, out var jtis))
        {
            jtis.TryRemove(jti, out _);
        }
    }
}
