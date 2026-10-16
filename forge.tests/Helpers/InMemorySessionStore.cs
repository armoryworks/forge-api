using System.Collections.Concurrent;

using Forge.Core.Interfaces;

namespace Forge.Tests.Helpers;

/// <summary>
/// Dictionary-backed <see cref="ISessionStore"/> for host-level tests. The production
/// DbSessionStore relies on ExecuteUpdate/ExecuteDelete, which the EF InMemory provider
/// cannot run, so tests that drive real JWTs through the pipeline swap this in.
/// </summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, (int UserId, DateTimeOffset ExpiresAt, bool Revoked)> _sessions = new();

    public Task CreateSessionAsync(int userId, string jti, DateTimeOffset expiresAt,
        string? authMethod = null, string? ipAddress = null, string? userAgent = null,
        CancellationToken ct = default)
    {
        _sessions[jti] = (userId, expiresAt, false);
        return Task.CompletedTask;
    }

    public Task<bool> ValidateSessionAsync(string jti, CancellationToken ct = default)
        => Task.FromResult(_sessions.TryGetValue(jti, out var s) && !s.Revoked && s.ExpiresAt > DateTimeOffset.UtcNow);

    public Task RevokeSessionAsync(string jti, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(jti, out var s))
            _sessions[jti] = s with { Revoked = true };
        return Task.CompletedTask;
    }

    public Task RevokeAllUserSessionsAsync(int userId, CancellationToken ct = default)
    {
        foreach (var (jti, s) in _sessions.Where(kv => kv.Value.UserId == userId).ToList())
            _sessions[jti] = s with { Revoked = true };
        return Task.CompletedTask;
    }

    public Task<string?> UpdateSessionJtiAsync(string oldJti, string newJti, DateTimeOffset newExpiresAt,
        CancellationToken ct = default)
    {
        if (!_sessions.TryRemove(oldJti, out var s) || s.Revoked || s.ExpiresAt <= DateTimeOffset.UtcNow)
            return Task.FromResult<string?>(null);
        _sessions[newJti] = (s.UserId, newExpiresAt, false);
        return Task.FromResult<string?>(newJti);
    }

    public bool HasLiveSession(int userId)
        => _sessions.Values.Any(s => s.UserId == userId && !s.Revoked && s.ExpiresAt > DateTimeOffset.UtcNow);
}
