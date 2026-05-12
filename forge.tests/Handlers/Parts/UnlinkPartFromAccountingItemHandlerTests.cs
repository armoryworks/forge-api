using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Parts;

public class UnlinkPartFromAccountingItemHandlerTests
{
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly UnlinkPartFromAccountingItemHandler _handler;

    public UnlinkPartFromAccountingItemHandlerTests()
    {
        _handler = new UnlinkPartFromAccountingItemHandler(
            _partRepo.Object,
            Mock.Of<ILogger<UnlinkPartFromAccountingItemHandler>>());
    }

    [Fact]
    public async Task Handle_ValidCommand_ClearsAccountingFields()
    {
        // Arrange
        var part = new Part
        {
            Id = 1,
            PartNumber = "P-001",
            Description = "Test Part",
            ExternalId = "ext-123",
            ExternalRef = "QB-REF-001",
            Provider = "quickbooks",
        };

        _partRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new UnlinkPartFromAccountingItemCommand(1);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        part.ExternalId.Should().BeNull();
        part.ExternalRef.Should().BeNull();
        part.Provider.Should().BeNull();

        _partRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PartNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _partRepo.Setup(r => r.FindAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Part?)null);

        var command = new UnlinkPartFromAccountingItemCommand(999);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Part 999*");
    }

    [Fact]
    public async Task Handle_AlreadyUnlinked_StillSavesSuccessfully()
    {
        // Arrange
        var part = new Part
        {
            Id = 1,
            PartNumber = "P-001",
            Description = "Test Part",
            ExternalId = null,
            ExternalRef = null,
            Provider = null,
        };

        _partRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new UnlinkPartFromAccountingItemCommand(1);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        part.ExternalId.Should().BeNull();
        _partRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
