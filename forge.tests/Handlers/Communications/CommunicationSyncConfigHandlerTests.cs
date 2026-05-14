using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Communications;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Communications;

/// <summary>
/// Wave 8 — handler tests for the connection-list / connect / disconnect
/// surface. Focus: tenant isolation (only the calling user's rows return),
/// soft-delete semantics, and the duplicate-connection guard.
/// </summary>
public class CommunicationSyncConfigHandlerTests
{
    private readonly Data.Context.AppDbContext _db;
    private const int UserA = 101;
    private const int UserB = 102;

    public CommunicationSyncConfigHandlerTests()
    {
        _db = TestDbContextFactory.Create();
    }

    private async Task SeedRowAsync(int userId, CommunicationKind kind, string providerId, string? externalAcct = null)
    {
        _db.CommunicationSyncConfigs.Add(new CommunicationSyncConfig
        {
            UserId = userId,
            Kind = kind,
            ProviderId = providerId,
            DisplayLabel = $"{providerId}-label",
            ExternalAccountId = externalAcct,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_ReturnsOnlyCallingUsersRows()
    {
        await SeedRowAsync(UserA, CommunicationKind.Email, "imap");
        await SeedRowAsync(UserA, CommunicationKind.Voice, "twilio");
        await SeedRowAsync(UserB, CommunicationKind.Email, "gmail");

        _db.CurrentUserId = UserA;
        var handler = new GetCommunicationSyncConfigsHandler(_db);

        var result = await handler.Handle(new GetCommunicationSyncConfigsQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(c => c.UserId == UserA);
    }

    [Fact]
    public async Task Get_ThrowsWhenNoCurrentUser()
    {
        _db.CurrentUserId = null;
        var handler = new GetCommunicationSyncConfigsHandler(_db);

        var act = async () => await handler.Handle(new GetCommunicationSyncConfigsQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_PersistsRowDisconnectedByDefault()
    {
        _db.CurrentUserId = UserA;
        var handler = new CreateCommunicationSyncConfigHandler(_db);

        var result = await handler.Handle(
            new CreateCommunicationSyncConfigCommand(
                CommunicationKind.Email, "imap", "Work mailbox", "alice@work.com", null),
            CancellationToken.None);

        result.IsConnected.Should().BeFalse();
        result.UserId.Should().Be(UserA);
        result.ProviderId.Should().Be("imap");

        var saved = _db.CommunicationSyncConfigs.Single();
        saved.UserId.Should().Be(UserA);
        saved.Kind.Should().Be(CommunicationKind.Email);
        saved.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Create_RejectsDuplicate_SameKindProviderExternalAcct()
    {
        await SeedRowAsync(UserA, CommunicationKind.Email, "imap", "alice@work.com");

        _db.CurrentUserId = UserA;
        var handler = new CreateCommunicationSyncConfigHandler(_db);

        var act = async () => await handler.Handle(
            new CreateCommunicationSyncConfigCommand(
                CommunicationKind.Email, "imap", "Another label", "alice@work.com", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Create_AllowsSameProvider_DifferentExternalAcct()
    {
        // Two mailboxes from the same provider — common case for someone
        // with a personal + work Gmail.
        await SeedRowAsync(UserA, CommunicationKind.Email, "gmail", "personal@gmail.com");

        _db.CurrentUserId = UserA;
        var handler = new CreateCommunicationSyncConfigHandler(_db);

        var result = await handler.Handle(
            new CreateCommunicationSyncConfigCommand(
                CommunicationKind.Email, "gmail", "Work", "work@gmail.com", null),
            CancellationToken.None);

        result.Id.Should().BeGreaterThan(0);
        _db.CommunicationSyncConfigs.Where(c => c.UserId == UserA).Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_SoftDeletesAndHidesFromList()
    {
        await SeedRowAsync(UserA, CommunicationKind.Email, "imap");
        var saved = _db.CommunicationSyncConfigs.Single();

        _db.CurrentUserId = UserA;
        var deleteHandler = new DeleteCommunicationSyncConfigHandler(_db);
        var listHandler = new GetCommunicationSyncConfigsHandler(_db);

        await deleteHandler.Handle(new DeleteCommunicationSyncConfigCommand(saved.Id), CancellationToken.None);
        var afterDelete = await listHandler.Handle(new GetCommunicationSyncConfigsQuery(), CancellationToken.None);

        afterDelete.Should().BeEmpty();
        // Row still in the table — global query filter hides it on read.
        // (InMemory doesn't enforce the filter strictly the same way as
        // Postgres, but the DeletedAt stamp is visible regardless.)
        _db.CommunicationSyncConfigs.IgnoreQueryFilters().Single().DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_RefusesAnotherUsersRow()
    {
        await SeedRowAsync(UserB, CommunicationKind.Email, "imap");
        var foreignRow = _db.CommunicationSyncConfigs.Single();

        _db.CurrentUserId = UserA;
        var handler = new DeleteCommunicationSyncConfigHandler(_db);

        var act = async () => await handler.Handle(
            new DeleteCommunicationSyncConfigCommand(foreignRow.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
