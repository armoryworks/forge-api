using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Interfaces;

namespace Forge.Tests.Handlers.Parts;

public class LinkPartToAccountingItemHandlerTests
{
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly Mock<IAccountingProviderFactory> _providerFactory = new();
    private readonly LinkPartToAccountingItemHandler _handler;

    public LinkPartToAccountingItemHandlerTests()
    {
        _handler = new LinkPartToAccountingItemHandler(
            _partRepo.Object,
            _providerFactory.Object,
            Mock.Of<ILogger<LinkPartToAccountingItemHandler>>());
    }

    [Fact]
    public async Task Handle_ValidCommand_LinksPartToAccountingItem()
    {
        // Arrange
        var part = new Part { Id = 1, PartNumber = "P-001", Description = "Test Part" };
        var mockProvider = new Mock<IAccountingService>();
        mockProvider.Setup(p => p.ProviderId).Returns("quickbooks");

        _partRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(part);
        _providerFactory.Setup(f => f.GetActiveProviderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockProvider.Object);

        var command = new LinkPartToAccountingItemCommand(1, "ext-123", "QB-REF-001");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        part.ExternalId.Should().Be("ext-123");
        part.ExternalRef.Should().Be("QB-REF-001");
        part.Provider.Should().Be("quickbooks");

        _partRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoActiveProvider_SetsProviderToNull()
    {
        // Arrange
        var part = new Part { Id = 1, PartNumber = "P-001", Description = "Test Part" };

        _partRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(part);
        _providerFactory.Setup(f => f.GetActiveProviderAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAccountingService?)null);

        var command = new LinkPartToAccountingItemCommand(1, "ext-123", "REF-001");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        part.ExternalId.Should().Be("ext-123");
        part.ExternalRef.Should().Be("REF-001");
        part.Provider.Should().BeNull();

        _partRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PartNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _partRepo.Setup(r => r.FindAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Part?)null);

        var command = new LinkPartToAccountingItemCommand(999, "ext-123", "REF-001");

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Part 999*");
    }
}
