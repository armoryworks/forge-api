using FluentAssertions;
using Moq;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class UpdatePurchaseOrderNumberTests
{
    private readonly Mock<IPurchaseOrderRepository> _repo = new();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdatePurchaseOrderHandler _handler;

    public UpdatePurchaseOrderNumberTests()
    {
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _handler = new UpdatePurchaseOrderHandler(_repo.Object, _settings.Object, _identifiers.Object, _db);
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("purchase_orders.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "purchase_orders.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private void SetupRepoFor(PurchaseOrder po) =>
        _repo.Setup(r => r.FindAsync(po.Id, It.IsAny<CancellationToken>())).ReturnsAsync(po);

    [Fact]
    public async Task Renames_the_po_number_when_manual_numbers_allowed_and_unique()
    {
        var po = new PurchaseOrder { Id = 1, PONumber = "PO-00001", Status = PurchaseOrderStatus.Draft };
        SetupRepoFor(po);
        AllowManualNumbers(true);
        _repo.Setup(r => r.PONumberExistsAsync("ACME-PO", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(new UpdatePurchaseOrderCommand(1, null, null, PONumber: "ACME-PO"), CancellationToken.None);

        po.PONumber.Should().Be("ACME-PO");
        _identifiers.Verify(i => i.RenameAsync(BusinessEntityType.PurchaseOrder, 1, "ACME-PO", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rejects_a_duplicate_po_number()
    {
        var po = new PurchaseOrder { Id = 1, PONumber = "PO-00001", Status = PurchaseOrderStatus.Draft };
        SetupRepoFor(po);
        AllowManualNumbers(true);
        _repo.Setup(r => r.PONumberExistsAsync("TAKEN", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _handler.Handle(new UpdatePurchaseOrderCommand(1, null, null, PONumber: "TAKEN"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("already in use");
        po.PONumber.Should().Be("PO-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_when_manual_numbers_disabled()
    {
        var po = new PurchaseOrder { Id = 1, PONumber = "PO-00001", Status = PurchaseOrderStatus.Draft };
        SetupRepoFor(po);
        AllowManualNumbers(false);

        var act = () => _handler.Handle(new UpdatePurchaseOrderCommand(1, null, null, PONumber: "ACME-PO"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("disabled");
        po.PONumber.Should().Be("PO-00001");
    }

    [Fact]
    public async Task Rejects_a_rename_once_the_po_leaves_draft()
    {
        // Lifecycle gate — the PO number is editable only while the PO is in Draft.
        var po = new PurchaseOrder { Id = 1, PONumber = "PO-00001", Status = PurchaseOrderStatus.Submitted };
        SetupRepoFor(po);
        AllowManualNumbers(true);
        _repo.Setup(r => r.PONumberExistsAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => _handler.Handle(new UpdatePurchaseOrderCommand(1, null, null, PONumber: "ACME-PO"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Draft");
        po.PONumber.Should().Be("PO-00001");
    }
}
