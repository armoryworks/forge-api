using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using QBEngineer.Api.Features.Communications;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Interfaces.Communications;
using QBEngineer.Core.Models.Communications;
using QBEngineer.Tests.Helpers;

namespace QBEngineer.Tests.Handlers.Communications;

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
        // The provider receives the row owner's userId, not the (null)
        // caller — so the matcher attributions stay correct under
        // Hangfire-driven syncs.
        stub.LastSyncedUserId.Should().Be(99);
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

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class StubProvider(string providerId, CommunicationKind kind, int eventCount = 0) : ICommunicationSyncProvider
    {
        public string ProviderId { get; } = providerId;
        public CommunicationKind Kind { get; } = kind;
        public int SyncCalls { get; private set; }
        public int? LastSyncedUserId { get; private set; }

        public Task<string?> StartAuthAsync(int userId, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<bool> CompleteAuthAsync(int userId, string code, CancellationToken ct) => Task.FromResult(true);
        public Task<int> SyncRecentAsync(int userId, CancellationToken ct)
        {
            SyncCalls++;
            LastSyncedUserId = userId;
            return Task.FromResult(eventCount);
        }
        public Task IngestWebhookEventAsync(string rawPayload, CancellationToken ct) => Task.CompletedTask;
    }
}
