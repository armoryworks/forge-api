using Bogus;
using FluentAssertions;
using Moq;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class CreatePurchaseOrderHandlerTests
{
    private readonly Mock<IPurchaseOrderRepository> _poRepo = new();
    private readonly Mock<IVendorRepository> _vendorRepo = new();
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CreatePurchaseOrderHandler _handler;

    private readonly Faker _faker = new();

    public CreatePurchaseOrderHandlerTests()
    {
        _handler = new CreatePurchaseOrderHandler(
            _poRepo.Object, _vendorRepo.Object, _partRepo.Object,
            Mock.Of<IBarcodeService>(),
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            _db);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesPOAndReturnsListItem()
    {
        // Arrange
        var vendorId = _faker.Random.Int(1, 100);
        var partId = _faker.Random.Int(1, 100);
        var poNumber = $"PO-{_faker.Random.Int(1000, 9999)}";
        var vendor = new Vendor { Id = vendorId, CompanyName = _faker.Company.CompanyName() };
        var part = new Part { Id = partId, PartNumber = "P-001", Description = "Test Part" };

        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(poNumber);
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(part);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, "Test notes",
            [new CreatePurchaseOrderLineModel(partId, null, 10, 5.50m, null)]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.PONumber.Should().Be(poNumber);
        result.VendorId.Should().Be(vendorId);
        result.VendorName.Should().Be(vendor.CompanyName);
        result.LineCount.Should().Be(1);
        result.TotalOrdered.Should().Be(10);

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.PONumber == poNumber &&
            po.VendorId == vendorId &&
            po.Notes == "Test notes" &&
            po.Lines.Count == 1
        ), It.IsAny<CancellationToken>()), Times.Once);

        _poRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_VendorNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _vendorRepo.Setup(r => r.FindAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vendor?)null);

        var command = new CreatePurchaseOrderCommand(
            999, null, null,
            [new CreatePurchaseOrderLineModel(1, null, 5, 1.00m, null)]);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Vendor 999*");
    }

    [Fact]
    public async Task Handle_PartNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var vendorId = 1;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Test Vendor" };

        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Part?)null);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(999, null, 5, 1.00m, null)]);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Part 999*");
    }

    [Fact]
    public async Task Handle_LineWithoutDescription_UsesPartDescription()
    {
        // Arrange
        var vendorId = 1;
        var partId = 2;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part = new Part { Id = partId, PartNumber = "P-001", Description = "Default Part Description" };

        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(partId, null, 1, 10m, null)]);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.Lines.First().Description == "Default Part Description"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LineWithCustomDescription_UsesProvidedDescription()
    {
        // Arrange
        var vendorId = 1;
        var partId = 2;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part = new Part { Id = partId, PartNumber = "P-001", Description = "Part Desc" };

        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(partId, "Custom Description", 1, 10m, null)]);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.Lines.First().Description == "Custom Description"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LineWithManualOverrideReason_PersistsReason()
    {
        // Arrange
        var vendorId = 1;
        var partId = 2;
        const string reason = "Negotiated one-off rate with vendor";
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part = new Part { Id = partId, PartNumber = "P-001", Description = "Part Desc" };

        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(partId, null, 1, 10m, null, ManualOverrideReason: reason)]);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.Lines.First().ManualOverrideReason == reason
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MultipleLines_CreatesAllLines()
    {
        // Arrange
        var vendorId = 1;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part1 = new Part { Id = 1, PartNumber = "P-001", Description = "Part 1" };
        var part2 = new Part { Id = 2, PartNumber = "P-002", Description = "Part 2" };

        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(part1);
        _partRepo.Setup(r => r.FindAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(part2);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [
                new CreatePurchaseOrderLineModel(1, null, 5, 10m, null),
                new CreatePurchaseOrderLineModel(2, null, 3, 20m, null),
            ]);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.LineCount.Should().Be(2);
        result.TotalOrdered.Should().Be(8);
    }

    [Fact]
    public async Task Handle_NoVendorPart_DefaultsToFobOriginAndUsd()
    {
        // Bought-parts PR2.5: when no preferred VendorPart row exists for the
        // (vendor, part) and the caller didn't pass header values, the PO
        // should default to FOB_Origin / USD via the entity's defaults.
        var vendorId = 1;
        var partId = 2;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part = new Part { Id = partId, PartNumber = "P-001", Description = "Part" };
        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(partId, null, 1, 10m, null)]);

        await _handler.Handle(command, CancellationToken.None);

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.Incoterm == Forge.Core.Enums.Incoterm.FOB_Origin
            && po.QuoteCurrency == "USD"
            && po.EstimatedFreight == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_VendorPartExists_DefaultsFromPreferred()
    {
        // When a VendorPart row exists for (vendor, part), the PO defaults
        // pull Incoterm + Currency from that row when the caller doesn't
        // supply them.
        var vendorId = 7;
        var partId = 9;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part = new Part { Id = partId, PartNumber = "P-009", Description = "Part" };
        _db.VendorParts.Add(new VendorPart
        {
            VendorId = vendorId,
            PartId = partId,
            Currency = "EUR",
            Incoterm = Forge.Core.Enums.Incoterm.DAP,
        });
        await _db.SaveChangesAsync();

        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0042");
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(partId, null, 1, 10m, null)]);

        await _handler.Handle(command, CancellationToken.None);

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.Incoterm == Forge.Core.Enums.Incoterm.DAP
            && po.QuoteCurrency == "EUR"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SetsManualOriginWithCurrentUser()
    {
        // S4b provenance — the manual-create path stamps Manual + the
        // creating user from the JWT claims.
        var vendorId = 1;
        var partId = 2;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part = new Part { Id = partId, PartNumber = "P-001", Description = "Part" };
        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "42")])),
        };
        var accessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

        var handler = new CreatePurchaseOrderHandler(
            _poRepo.Object, _vendorRepo.Object, _partRepo.Object,
            Mock.Of<IBarcodeService>(),
            Mock.Of<MediatR.IMediator>(),
            accessor.Object,
            _db);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(partId, null, 1, 10m, null)]);

        await handler.Handle(command, CancellationToken.None);

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.OriginSource == Forge.Core.Enums.PoOriginSource.Manual
            && po.OriginUserId == 42
            && po.OriginReference == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoHttpContext_ManualOriginWithoutUser()
    {
        // S4b provenance — when no user principal exists (the shared test
        // handler mocks an empty accessor), OriginUserId stays null but the
        // origin is still declared Manual.
        var vendorId = 1;
        var partId = 2;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part = new Part { Id = partId, PartNumber = "P-001", Description = "Part" };
        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(partId, null, 1, 10m, null)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.OriginSource.Should().Be("Manual");

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.OriginSource == Forge.Core.Enums.PoOriginSource.Manual
            && po.OriginUserId == null
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerOverridesHeaderFields_UsesProvidedValues()
    {
        var vendorId = 1;
        var partId = 2;
        var vendor = new Vendor { Id = vendorId, CompanyName = "Vendor" };
        var part = new Part { Id = partId, PartNumber = "P-001", Description = "Part" };
        _vendorRepo.Setup(r => r.FindAsync(vendorId, It.IsAny<CancellationToken>())).ReturnsAsync(vendor);
        _poRepo.Setup(r => r.GenerateNextPONumberAsync(It.IsAny<CancellationToken>())).ReturnsAsync("PO-0001");
        _partRepo.Setup(r => r.FindAsync(partId, It.IsAny<CancellationToken>())).ReturnsAsync(part);

        var command = new CreatePurchaseOrderCommand(
            vendorId, null, null,
            [new CreatePurchaseOrderLineModel(partId, null, 1, 10m, null)],
            Forge.Core.Enums.Incoterm.CIF, 25.00m, "GBP");

        await _handler.Handle(command, CancellationToken.None);

        _poRepo.Verify(r => r.AddAsync(It.Is<PurchaseOrder>(po =>
            po.Incoterm == Forge.Core.Enums.Incoterm.CIF
            && po.QuoteCurrency == "GBP"
            && po.EstimatedFreight == 25.00m
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}
