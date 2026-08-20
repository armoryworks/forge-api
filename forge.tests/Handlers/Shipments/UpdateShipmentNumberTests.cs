using FluentAssertions;
using Moq;
using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

public class UpdateShipmentNumberTests
{
    private readonly Mock<IShipmentRepository> _repo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateShipmentHandler _handler;

    public UpdateShipmentNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _handler = new UpdateShipmentHandler(_repo.Object, _settings.Object, _identifiers.Object, _db);
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("shipments.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "shipments.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoFor(Shipment shipment) =>
        _repo.Setup(r => r.FindWithDetailsAsync(shipment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shipment);

    [Fact]
    public async Task Renames_the_shipment_number_when_manual_numbers_allowed_and_unique()
    {
        var shipment = new Shipment { Id = 1, ShipmentNumber = "SH-00001", Status = ShipmentStatus.Pending };
        SetupRepoFor(shipment);
        AllowManualNumbers(true);
        _repo.Setup(r => r.ShipmentNumberExistsAsync("ACME-SH", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(new UpdateShipmentCommand(1, null, null, null, null, null, ShipmentNumber: "ACME-SH"), CancellationToken.None);

        shipment.ShipmentNumber.Should().Be("ACME-SH");
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.Shipment, 1, "ACME-SH", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_shipment_number()
    {
        var shipment = new Shipment { Id = 1, ShipmentNumber = "SH-00001", Status = ShipmentStatus.Pending };
        SetupRepoFor(shipment);
        AllowManualNumbers(true);
        _repo.Setup(r => r.ShipmentNumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(new UpdateShipmentCommand(1, null, null, null, null, null, ShipmentNumber: "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        shipment.ShipmentNumber.Should().Be("SH-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var shipment = new Shipment { Id = 1, ShipmentNumber = "SH-00001", Status = ShipmentStatus.Pending };
        SetupRepoFor(shipment);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(new UpdateShipmentCommand(1, null, null, null, null, null, ShipmentNumber: "ACME-SH"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        shipment.ShipmentNumber.Should().Be("SH-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_once_the_shipment_has_shipped()
    {
        // Lifecycle gate — the shipment number is editable only before it ships (Pending/Packed).
        var shipment = new Shipment { Id = 1, ShipmentNumber = "SH-00001", Status = ShipmentStatus.Shipped };
        SetupRepoFor(shipment);
        AllowManualNumbers(true);
        _repo.Setup(r => r.ShipmentNumberExistsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.Handle(new UpdateShipmentCommand(1, null, null, null, null, null, ShipmentNumber: "ACME-SH"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("shipped");
        shipment.ShipmentNumber.Should().Be("SH-00001");
    }
}
