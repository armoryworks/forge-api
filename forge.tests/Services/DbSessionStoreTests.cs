using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

/// <summary>
/// DbSessionStore against real Postgres — the store's revoke and JTI-rotation
/// paths use ExecuteUpdate/ExecuteDelete, which the InMemory provider cannot
/// run. Each test uses its own cache instance so validation caching is
/// exercised deliberately, never accidentally.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DbSessionStoreTests(PostgresFixture fixture)
{
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private (DbSessionStore store, MutableClock clock, IMemoryCache cache) CreateStore()
    {
        var clock = new MutableClock();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new DbSessionStore(
            fixture.CreateContext(), cache, clock, NullLogger<DbSessionStore>.Instance);
        return (store, clock, cache);
    }

    [Fact]
    public async Task Created_session_validates_until_expiry()
    {
        var (store, clock, _) = CreateStore();
        var jti = Guid.NewGuid().ToString("N");

        await store.CreateSessionAsync(9001, jti, clock.UtcNow.AddHours(1),
            authMethod: "password", ipAddress: "10.0.0.1", userAgent: "test-agent");

        (await store.ValidateSessionAsync(jti)).Should().BeTrue();
    }

    [Fact]
    public async Task Expired_session_fails_validation()
    {
        var (store, clock, _) = CreateStore();
        var jti = Guid.NewGuid().ToString("N");

        await store.CreateSessionAsync(9002, jti, clock.UtcNow.AddSeconds(30));
        clock.UtcNow = clock.UtcNow.AddMinutes(5);

        (await store.ValidateSessionAsync(jti)).Should().BeFalse();
    }

    [Fact]
    public async Task Revoked_session_fails_validation_immediately()
    {
        var (store, clock, _) = CreateStore();
        var jti = Guid.NewGuid().ToString("N");

        await store.CreateSessionAsync(9003, jti, clock.UtcNow.AddHours(1));
        (await store.ValidateSessionAsync(jti)).Should().BeTrue();

        await store.RevokeSessionAsync(jti);

        (await store.ValidateSessionAsync(jti)).Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_all_kills_every_session_for_the_user_only()
    {
        var (store, clock, _) = CreateStore();
        var mine1 = Guid.NewGuid().ToString("N");
        var mine2 = Guid.NewGuid().ToString("N");
        var theirs = Guid.NewGuid().ToString("N");

        await store.CreateSessionAsync(9004, mine1, clock.UtcNow.AddHours(1));
        await store.CreateSessionAsync(9004, mine2, clock.UtcNow.AddHours(1));
        await store.CreateSessionAsync(9005, theirs, clock.UtcNow.AddHours(1));

        await store.RevokeAllUserSessionsAsync(9004);

        (await store.ValidateSessionAsync(mine1)).Should().BeFalse();
        (await store.ValidateSessionAsync(mine2)).Should().BeFalse();
        (await store.ValidateSessionAsync(theirs)).Should().BeTrue();
    }

    [Fact]
    public async Task Jti_rotation_replaces_old_with_new()
    {
        var (store, clock, _) = CreateStore();
        var oldJti = Guid.NewGuid().ToString("N");
        var newJti = Guid.NewGuid().ToString("N");

        await store.CreateSessionAsync(9006, oldJti, clock.UtcNow.AddHours(1));

        var result = await store.UpdateSessionJtiAsync(oldJti, newJti, clock.UtcNow.AddHours(2));

        result.Should().Be(newJti);
        (await store.ValidateSessionAsync(newJti)).Should().BeTrue();
        (await store.ValidateSessionAsync(oldJti)).Should().BeFalse();
    }

    [Fact]
    public async Task Rotating_an_unknown_or_revoked_jti_returns_null()
    {
        var (store, clock, _) = CreateStore();
        var jti = Guid.NewGuid().ToString("N");

        (await store.UpdateSessionJtiAsync(jti, Guid.NewGuid().ToString("N"),
            clock.UtcNow.AddHours(1))).Should().BeNull();

        await store.CreateSessionAsync(9007, jti, clock.UtcNow.AddHours(1));
        await store.RevokeSessionAsync(jti);

        (await store.UpdateSessionJtiAsync(jti, Guid.NewGuid().ToString("N"),
            clock.UtcNow.AddHours(1))).Should().BeNull();
    }

    [Fact]
    public async Task Validation_cache_does_not_outlive_short_expiry()
    {
        var (store, clock, _) = CreateStore();
        var jti = Guid.NewGuid().ToString("N");

        // Expires inside the 60s cache window: the cache TTL must clamp to
        // the session expiry, not extend it.
        await store.CreateSessionAsync(9008, jti, clock.UtcNow.AddSeconds(1));
        (await store.ValidateSessionAsync(jti)).Should().BeTrue();

        clock.UtcNow = clock.UtcNow.AddSeconds(2);
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        (await store.ValidateSessionAsync(jti)).Should().BeFalse();
    }
}
