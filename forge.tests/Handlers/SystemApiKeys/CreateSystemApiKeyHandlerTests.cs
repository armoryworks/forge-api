using FluentAssertions;
using Moq;

using Forge.Api.Features.SystemApiKeys;
using Forge.Api.Services;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SystemApiKeys;

public class CreateSystemApiKeyHandlerTests
{
    private readonly Mock<ISystemAuditWriter> _auditWriter = new();

    private CreateSystemApiKeyHandler MakeHandler(AppDbContext db) =>
        new(db, _auditWriter.Object);

    private static async Task<int> SeedActiveUserAsync(AppDbContext db, string email = "svc@example.local")
    {
        var user = new ApplicationUser
        {
            Id = 0,  // EF assigns
            UserName = email,
            Email = email,
            FirstName = "Test",
            LastName = "Service",
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Handle_ValidRequest_IssuesKey_WithFskPrefix_And_AuditRow()
    {
        using var db = TestDbContextFactory.Create();
        var userId = await SeedActiveUserAsync(db);
        var handler = MakeHandler(db);

        var result = await handler.Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "test key",
                UserId = userId,
            }),
            CancellationToken.None);

        result.Id.Should().BeGreaterThan(0);
        result.PlaintextKey.Should().StartWith("fsk_");
        result.PlaintextKey.Length.Should().BeGreaterThan(20,
            "32-byte base64url + 'fsk_' prefix is comfortably > 20 chars");
        result.KeyPrefix.Should().HaveLength(12)
            .And.Be(result.PlaintextKey[..12]);
        result.UserId.Should().Be(userId);
        result.Name.Should().Be("test key");

        var persisted = await db.SystemApiKeys.FindAsync(result.Id);
        persisted.Should().NotBeNull();
        persisted!.KeyHash.Should().NotBeNullOrEmpty();
        persisted.KeyHash.Should().NotContain(result.PlaintextKey,
            "the persisted row must hold only the PBKDF2 hash, never the plaintext");
        persisted.IsActive.Should().BeTrue();

        _auditWriter.Verify(
            x => x.WriteAsync(
                "SystemApiKeyIssued",
                It.IsAny<int>(),
                "SystemApiKey",
                result.Id,
                It.Is<string>(s => s.Contains(result.KeyPrefix) && !s.Contains(result.PlaintextKey)),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "issuance must emit an audit row carrying the prefix but NEVER the plaintext");
    }

    [Fact]
    public async Task Handle_UnknownUserId_ThrowsKeyNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var handler = MakeHandler(db);

        var act = () => handler.Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "test",
                UserId = 99999,
            }),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*99999*");
    }

    [Fact]
    public async Task Handle_InactiveUser_ThrowsKeyNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var inactive = new ApplicationUser
        {
            UserName = "inactive@example.local",
            Email = "inactive@example.local",
            IsActive = false,
        };
        db.Users.Add(inactive);
        await db.SaveChangesAsync();

        var handler = MakeHandler(db);

        var act = () => handler.Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "test",
                UserId = inactive.Id,
            }),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>(
            "issuing a key for a deactivated user is refused — the bound auth check would always fail");
    }

    [Fact]
    public async Task Handle_TwoCalls_ProduceDistinctKeys_AndDistinctPrefixes()
    {
        using var db = TestDbContextFactory.Create();
        var userId = await SeedActiveUserAsync(db);
        var handler = MakeHandler(db);

        var first = await handler.Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "first", UserId = userId,
            }),
            CancellationToken.None);

        var second = await handler.Handle(
            new CreateSystemApiKeyCommand(new CreateSystemApiKeyRequestModel
            {
                Name = "second", UserId = userId,
            }),
            CancellationToken.None);

        first.PlaintextKey.Should().NotBe(second.PlaintextKey);
        first.KeyPrefix.Should().NotBe(second.KeyPrefix,
            "32-byte random plaintexts effectively never collide on the 12-char prefix");
    }
}
