using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.AutoPo;
using Forge.Api.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Data.Services;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

public class AutoPurchaseOrderJobApprovalTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task SeedDemandAsync()
    {
        var vendor = new Vendor { CompanyName = "AutoPO Vendor", AutoPoMode = AutoPoMode.Automatic };
        _db.Vendors.Add(vendor);
        var parent = new Part
        {
            PartNumber = "PARENT-A", Name = "Parent",
            ProcurementSource = ProcurementSource.Make, InventoryClass = InventoryClass.FinishedGood, Status = PartStatus.Active,
        };
        var child = new Part
        {
            PartNumber = "CHILD-A", Name = "Child",
            ProcurementSource = ProcurementSource.Buy, InventoryClass = InventoryClass.Component, Status = PartStatus.Active,
        };
        _db.Parts.AddRange(parent, child);
        var customer = new Customer { Name = "Test Customer" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        child.PreferredVendorId = vendor.Id;
        _db.BOMLines.Add(new BOMLine { ParentPartId = parent.Id, ChildPartId = child.Id, Quantity = 2m, SourceType = BOMSourceType.Buy });
        var so = new SalesOrder
        {
            OrderNumber = "SO-APPROVAL-1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = DateTimeOffset.UtcNow.AddDays(30),
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        _db.SalesOrderLines.Add(new SalesOrderLine { SalesOrderId = so.Id, PartId = parent.Id, Description = "Parent", Quantity = 5m, UnitPrice = 1m });
        await _db.SaveChangesAsync();
    }

    private async Task SeedWorkflowAsync(decimal? autoApproveBelow = null)
    {
        _db.ApprovalWorkflows.Add(new ApprovalWorkflow
        {
            Name = "PO review", EntityType = "PurchaseOrder", IsActive = true,
            Steps = { new ApprovalStep { StepNumber = 1, Name = "Manager", ApproverType = ApproverType.Role, ApproverRole = "Manager", AutoApproveBelow = autoApproveBelow } },
        });
        await _db.SaveChangesAsync();
    }

    private AutoPurchaseOrderJob BuildJob(Mock<IPublisher> publisher, params string[] capabilities)
    {
        var settingsRepo = new Mock<ISystemSettingRepository>();
        settingsRepo.Setup(r => r.FindByKeyAsync("inventory:auto_po_enabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "inventory:auto_po_enabled", Value = "true" });
        settingsRepo.Setup(r => r.FindByKeyAsync("inventory:auto_po_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "inventory:auto_po_mode", Value = "Draft" });

        var poGen = new PurchaseOrderGenerator(
            _db, new PurchaseOrderRepository(_db), Mock.Of<IBarcodeService>(), Mock.Of<IBusinessIdentifierService>());

        return new AutoPurchaseOrderJob(
            _db,
            new SystemClock(),
            settingsRepo.Object,
            poGen,
            new PartSourcingResolver(_db),
            NullLogger<AutoPurchaseOrderJob>.Instance,
            new StubCapabilitySnapshotProvider(capabilities),
            new ApprovalService(_db, NullLogger<ApprovalService>.Instance),
            publisher.Object);
    }

    [Fact]
    public async Task Automatic_mode_holds_the_po_for_approval_when_a_workflow_applies()
    {
        await SeedDemandAsync();
        await SeedWorkflowAsync();

        await BuildJob(new Mock<IPublisher>(), "CAP-P2P-AUTOPO", "CAP-P2P-APPROVALS").Execute(CancellationToken.None);

        var po = _db.PurchaseOrders.Single();
        po.Status.Should().Be(PurchaseOrderStatus.PendingApproval);
        po.SubmittedDate.Should().BeNull();
        var request = _db.ApprovalRequests.Single();
        request.EntityType.Should().Be("PurchaseOrder");
        request.EntityId.Should().Be(po.Id);
        request.Status.Should().Be(ApprovalRequestStatus.Pending);
        request.RequestedById.Should().Be(AutoPurchaseOrderJob.SystemRequesterId);
    }

    [Fact]
    public async Task Automatic_mode_submits_when_no_workflow_applies()
    {
        await SeedDemandAsync();

        await BuildJob(new Mock<IPublisher>(), "CAP-P2P-AUTOPO", "CAP-P2P-APPROVALS").Execute(CancellationToken.None);

        var po = _db.PurchaseOrders.Single();
        po.Status.Should().Be(PurchaseOrderStatus.Submitted);
        po.SubmittedDate.Should().NotBeNull();
        _db.ApprovalRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Automatic_mode_submits_when_approvals_are_off_even_with_a_workflow()
    {
        await SeedDemandAsync();
        await SeedWorkflowAsync();

        await BuildJob(new Mock<IPublisher>(), "CAP-P2P-AUTOPO").Execute(CancellationToken.None);

        _db.PurchaseOrders.Single().Status.Should().Be(PurchaseOrderStatus.Submitted);
        _db.ApprovalRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Automatic_mode_submits_and_announces_an_auto_approved_request()
    {
        await SeedDemandAsync();
        await SeedWorkflowAsync(autoApproveBelow: 1000m);
        var publisher = new Mock<IPublisher>();

        await BuildJob(publisher, "CAP-P2P-AUTOPO", "CAP-P2P-APPROVALS").Execute(CancellationToken.None);

        _db.PurchaseOrders.Single().Status.Should().Be(PurchaseOrderStatus.Submitted);
        _db.ApprovalRequests.Single().Status.Should().Be(ApprovalRequestStatus.AutoApproved);
        publisher.Verify(p => p.Publish(
            It.Is<Forge.Api.Features.DomainEvents.ApprovalCompletedEvent>(e => e.EntityType == "PurchaseOrder" && e.Approved),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
