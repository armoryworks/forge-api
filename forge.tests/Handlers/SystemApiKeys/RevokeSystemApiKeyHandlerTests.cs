using FluentAssertions;
using Moq;

using Forge.Api.Features.SystemApiKeys;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SystemApiKeys;

public class RevokeSystemApiKeyHandlerTests
{
    private readonly Mock<ISystemAuditWriter> _auditWriter = new();

    [Fact]
    public async Task Handle_ActiveKey_FlipsIsActive_AndWritesAudit()
    {
        using var db = TestDbContextFactory.Create();
        var key = new SystemApiKey
        {
            Name = "test", KeyHash = "hash", KeyPrefix = "fsk_abc12345",
            UserId = 1, IsActive = true,
        };
        db.SystemApiKeys.Add(key);
        await db.SaveChangesAsync();

        var handler = new RevokeSystemApiKeyHandler(db, _auditWriter.Object);
        await handler.Handle(new RevokeSystemApiKeyCommand(key.Id), CancellationToken.None);

        var reloaded = await db.SystemApiKeys.FindAsync(key.Id);
        reloaded!.IsActive.Should().BeFalse();

        _auditWriter.Verify(
            x => x.WriteAsync(
                "SystemApiKeyRevoked",
                It.IsAny<int>(),
                "SystemApiKey",
                key.Id,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyRevoked_StillEmitsAuditRow()
    {
        // Idempotency contract: revoking a revoked key writes an audit row
        // so operators can see "yes, the revoke arrived" even on a no-op.
        using var db = TestDbContextFactory.Create();
        var key = new SystemApiKey
        {
            Name = "test", KeyHash = "hash", KeyPrefix = "fsk_def67890",
            UserId = 1, IsActive = false,
        };
        db.SystemApiKeys.Add(key);
        await db.SaveChangesAsync();

        var handler = new RevokeSystemApiKeyHandler(db, _auditWriter.Object);
        await handler.Handle(new RevokeSystemApiKeyCommand(key.Id), CancellationToken.None);

        _auditWriter.Verify(
            x => x.WriteAsync(
                "SystemApiKeyRevoked",
                It.IsAny<int>(),
                "SystemApiKey",
                key.Id,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownKey_ThrowsKeyNotFound()
    {
        using var db = TestDbContextFactory.Create();
        var handler = new RevokeSystemApiKeyHandler(db, _auditWriter.Object);

        var act = () => handler.Handle(
            new RevokeSystemApiKeyCommand(99999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
