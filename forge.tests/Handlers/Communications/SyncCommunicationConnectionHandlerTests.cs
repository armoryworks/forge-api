using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.Communications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Interfaces.Communications;
using Forge.Core.Models.Communications;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// Wave 8 — sync-handler tests. Cover provider resolution (by ProviderId+
/// Kind), LastSyncedAt stamp on success, ownership enforcement for HTTP
/// callers, and the "no provider registered" path that the planned-but-
/// not-implemented adapters will hit until they ship.
/// </summary>
public class SyncCommunicationConnectionHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly FixedClock _clock = new();

    public SyncCommunicationConnectionHandlerTests()
    {
        _db = TestDbContextFactory.Create();
    }

    private SyncCommunicationConnectionHandler MakeHandler(params ICommunicationSyncProvider[] providers)
        => new(_db, providers, _clock, NullLogger<SyncCommunicationConnectionHandler>.Instance);

    private async Task<CommunicationSyncConfig> SeedConnectionAsync(
        int userId, CommunicationKind kind, string providerId, bool isConnected = true)
    {
        var config = new CommunicationSyncConfig
        {
            UserId = userId, Kind = kind, ProviderId = providerId,
            IsConnected = isConnected,
        };
        _db.CommunicationSyncConfigs.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }

    [Fact]
    public async Task Sync_StampsLastSyncedAt_WhenProviderReturns()
    {
        var config = await SeedConnectionAsync(42, CommunicationKind.Email, "stub");
        _db.CurrentUserId = 42;

        var handler = MakeHandler(new StubProvider("stub", CommunicationKind.Email, eventCount: 3));

        var result = await handler.Handle(new SyncCommunicationConnectionCommand(config.Id), CancellationToken.None);

        result.EventCount.Should().Be(3);
        result.SyncedAt.Should().Be(_clock.UtcNow);
        var refreshed = _db.CommunicationSyncConfigs.Find(config.Id)!;
        refreshed.LastSyncedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task Sync_ResolvesByProviderIdAndKind_NotJustProviderId()
    {
        // Two providers share the same ProviderId across different Kinds —
        // resolution must consider both. (The mock email + voice providers
        // are the canonical example with "mock-email" / "mock-voip" but
        // future adapters may reuse a name.)
        var config = await SeedConnectionAsync(42, CommunicationKind.Voice, "shared-id");
        _db.CurrentUserId = 42;

        var emailStub = new StubProvider("shared-id", CommunicationKind.Email, eventCount: 99);
        var voiceStub = new StubProvider("shared-id", CommunicationKind.Voice, eventCount: 7);

        var handler = MakeHandler(emailStub, voiceStub);
        var result = await handler.Handle(new SyncCommunicationConnectionCommand(config.Id), CancellationToken.None);

        result.EventCount.Should().Be(7);
        emailStub.SyncCalls.Should().Be(0);
        voiceStub.SyncCalls.Should().Be(1);
    }

    [Fact]
    public async Task Sync_RejectsForeignConnection_WhenHttpCaller()
    {
        var foreignConfig = await SeedConnectionAsync(99, CommunicationKind.Email, "stub");
        _db.CurrentUserId = 42;

        var handler = MakeHandler(new StubProvider("stub", CommunicationKind.Email));

        var act = async () => await handler.Handle(
            new SyncCommunicationConnectionCommand(foreignConfig.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Sync_AllowsAnyConnection_WhenSystemCaller()
    {
        // CurrentUserId=null = Hangfire-driven sync. Should not be blocked
        // by the ownership check; it operates on every connected row.
        var foreignConfig = await SeedConnectionAsync(99, CommunicationKind.Email, "stub");
        _db.CurrentUserId = null;

        var stub = new StubProvider("stub", CommunicationKind.Email, eventCount: 1);
        var handler = MakeHandler(stub);

        var result = await handler.Handle(
            new SyncCommunicationConnectionCommand(foreignConfig.Id), CancellationToken.None);

        result.EventCount.Should().Be(1);
        stub.SyncCalls.Should().Be(1);
        // Provider receives the connection id; it can read the row's UserId
        // off the AppDbContext when it needs matcher attribution context.
        stub.LastSyncedConnectionId.Should().Be(foreignConfig.Id);
    }

    [Fact]
    public async Task Sync_ThrowsWhenNoProviderRegistered()
    {
        var config = await SeedConnectionAsync(42, CommunicationKind.Email, "imap"); // planned, no impl
        _db.CurrentUserId = 42;

        var handler = MakeHandler(); // no providers registered

        var act = async () => await handler.Handle(
            new SyncCommunicationConnectionCommand(config.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No registered ICommunicationSyncProvider*");
    }

    [Fact]
    public async Task Sync_PersistsLastError_OnProviderException()
    {
        var config = await SeedConnectionAsync(42, CommunicationKind.Email, "stub");
        _db.CurrentUserId = 42;

        var failing = new ThrowingProvider("stub", CommunicationKind.Email,
            new InvalidOperationException("IMAP authentication failed — bad password"));
        var handler = MakeHandler(failing);

        var act = async () => await handler.Handle(
            new SyncCommunicationConnectionCommand(config.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        var refreshed = _db.CommunicationSyncConfigs.Find(config.Id)!;
        refreshed.LastError.Should().Contain("authentication failed");
        refreshed.LastErrorAt.Should().Be(_clock.UtcNow);
        // Successful-sync timestamp is NOT bumped on failure — that would
        // hide the issue from a "last sync was 3 hours ago" indicator.
        refreshed.LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public async Task Sync_ClearsLastError_OnNextSuccess()
    {
        // Seed a row that already has a stale error message, then run a
        // successful sync. The error should be cleared so the UI doesn't
        // show a permanent red chip after the user fixed the issue.
        var config = await SeedConnectionAsync(42, CommunicationKind.Email, "stub");
        config.LastError = "stale: bad creds";
        config.LastErrorAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await _db.SaveChangesAsync();

        _db.CurrentUserId = 42;
        var handler = MakeHandler(new StubProvider("stub", CommunicationKind.Email, eventCount: 1));

        await handler.Handle(new SyncCommunicationConnectionCommand(config.Id), CancellationToken.None);

        var refreshed = _db.CommunicationSyncConfigs.Find(config.Id)!;
        refreshed.LastError.Should().BeNull();
        refreshed.LastErrorAt.Should().BeNull();
    }

    [Fact]
    public async Task Sync_TruncatesOverlongErrorMessage()
    {
        var config = await SeedConnectionAsync(42, CommunicationKind.Email, "stub");
        _db.CurrentUserId = 42;

        var bigMessage = new string('x', 5000);
        var handler = MakeHandler(new ThrowingProvider("stub", CommunicationKind.Email,
            new InvalidOperationException(bigMessage)));

        var act = async () => await handler.Handle(
            new SyncCommunicationConnectionCommand(config.Id), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var refreshed = _db.CommunicationSyncConfigs.Find(config.Id)!;
        refreshed.LastError!.Length.Should().BeLessThanOrEqualTo(1024);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class ThrowingProvider(string providerId, CommunicationKind kind, Exception toThrow) : ICommunicationSyncProvider
    {
        public string ProviderId { get; } = providerId;
        public CommunicationKind Kind { get; } = kind;

        public Task<string?> StartAuthAsync(int userId, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<bool> CompleteAuthAsync(int userId, string code, CancellationToken ct) => Task.FromResult(true);
        public Task<int> SyncRecentAsync(int connectionId, CancellationToken ct) => Task.FromException<int>(toThrow);
        public Task IngestWebhookEventAsync(string rawPayload, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubProvider(string providerId, CommunicationKind kind, int eventCount = 0) : ICommunicationSyncProvider
    {
        public string ProviderId { get; } = providerId;
        public CommunicationKind Kind { get; } = kind;
        public int SyncCalls { get; private set; }
        public int? LastSyncedConnectionId { get; private set; }

        public Task<string?> StartAuthAsync(int userId, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<bool> CompleteAuthAsync(int userId, string code, CancellationToken ct) => Task.FromResult(true);
        public Task<int> SyncRecentAsync(int connectionId, CancellationToken ct)
        {
            SyncCalls++;
            LastSyncedConnectionId = connectionId;
            return Task.FromResult(eventCount);
        }
        public Task IngestWebhookEventAsync(string rawPayload, CancellationToken ct) => Task.CompletedTask;
    }
}
