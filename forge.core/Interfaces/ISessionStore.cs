namespace Forge.Core.Interfaces;

public interface ISessionStore
{
    Task CreateSessionAsync(int userId, string jti, DateTimeOffset expiresAt,
        string? authMethod = null, string? ipAddress = null, string? userAgent = null,
        CancellationToken ct = default);

    Task<bool> ValidateSessionAsync(string jti, CancellationToken ct = default);

    Task RevokeSessionAsync(string jti, CancellationToken ct = default);

    Task RevokeAllUserSessionsAsync(int userId, CancellationToken ct = default);

    Task<string?> UpdateSessionJtiAsync(string oldJti, string newJti, DateTimeOffset newExpiresAt,
        CancellationToken ct = default);
}
