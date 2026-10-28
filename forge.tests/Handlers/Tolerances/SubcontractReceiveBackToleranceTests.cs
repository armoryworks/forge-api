using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Jobs;
using Forge.Api.Features.Tolerances;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Tolerances;

public class SubcontractReceiveBackToleranceTests
{
    private readonly JobOperationTestHarness _h = new();

    private ReceiveBackSubcontractHandler Handler(params string[] roles)
        => new(_h.Timers.Db, _h.Operations, _h.Timers.TimerStopService(), _h.BoardHub.Object,
            _h.Timers.Mediator.Object, _h.Timers.Clock.Object, _h.Backflush.Object, TestCaller.As(1, roles));

    private async Task<SubcontractOrder> SeedAsync(
        decimal sent = 100m, decimal? vendorOver = null, decimal? vendorUnder = null,
        decimal? lineOver = null, decimal? lineUnder = null)
    {
        var (job, routing) = await _h.AddJobWithRoutingAsync(sent);
        var operation = routing[1];
        var vendor = new Vendor { CompanyName = "Plating Co", OverReceiptTolerancePct = vendorOver, UnderReceiptTolerancePct = vendorUnder };
        _h.Timers.Db.Vendors.Add(vendor);
        await _h.Timers.Db.SaveChangesAsync();

        int? poId = null;
        if (lineOver is not null || lineUnder is not null)
        {
            var po = new PurchaseOrder { PONumber = "PO-SUB-1", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
            po.Lines.Add(new PurchaseOrderLine
            {
                Description = "Op 20 plating", OrderedQuantity = sent, UnitPrice = 2m,
                OverReceiptTolerancePct = lineOver, UnderReceiptTolerancePct = lineUnder,
            });
            _h.Timers.Db.PurchaseOrders.Add(po);
            await _h.Timers.Db.SaveChangesAsync();
            poId = po.Id;
        }

        _h.Timers.Db.JobOperations.Add(new JobOperation
        {
            JobId = job.Id, OperationId = operation.Id, StepNumber = operation.StepNumber, Title = operation.Title,
        });
        var order = new SubcontractOrder
        {
            JobId = job.Id,
            OperationId = operation.Id,
            VendorId = vendor.Id,
            PurchaseOrderId = poId,
            Quantity = sent,
            SentAt = _h.Timers.Now.AddDays(-3),
            Status = SubcontractStatus.Sent,
        };
        _h.Timers.Db.SubcontractOrders.Add(order);
        await _h.Timers.Db.SaveChangesAsync();
        return order;
    }

    private Task<SubcontractOrderResponseModel> ReceiveAsync(
        int orderId, decimal good, decimal scrap = 0m, bool confirm = false, string? reason = null,
        bool closeShort = false, string[]? roles = null)
        => Handler(roles ?? []).Handle(
            new ReceiveBackSubcontractCommand(orderId, new ReceiveBackRequestModel(
                good, null, PassedInspection: true, ScrapQuantity: scrap,
                ConfirmOverTolerance: confirm, OverToleranceReason: reason, CloseShort: closeShort)),
            CancellationToken.None);

    private Task<SubcontractOrder> OrderAsync(int id)
        => _h.Timers.Db.SubcontractOrders.AsNoTracking().SingleAsync(o => o.Id == id);

    [Fact]
    public async Task NoTolerance_ShortReceiveBack_StaysOpenForTheRest()
    {
        var order = await SeedAsync();

        var result = await ReceiveAsync(order.Id, good: 90m);

        result.Status.Should().Be(nameof(SubcontractStatus.PartiallyReceived));
        var after = await OrderAsync(order.Id);
        after.ReceivedAt.Should().BeNull();
        after.ReceivedQuantity.Should().Be(90m);
    }

    [Fact]
    public async Task NoTolerance_SecondReceiveBack_CompletesTheOrderOnceEverythingIsBack()
    {
        var order = await SeedAsync();
        await ReceiveAsync(order.Id, good: 90m);

        var result = await ReceiveAsync(order.Id, good: 8m, scrap: 2m);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        var after = await OrderAsync(order.Id);
        after.ReceivedAt.Should().Be(_h.Timers.Now);
        after.ReceivedQuantity.Should().Be(98m);
        after.ScrapQuantity.Should().Be(2m);
    }

    [Fact]
    public async Task NoTolerance_CloseShort_ClosesTheOrderBelowTheBand()
    {
        var order = await SeedAsync();

        var result = await ReceiveAsync(order.Id, good: 80m, closeShort: true);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        (await OrderAsync(order.Id)).ReceivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UnderInsideVendorTolerance_ClosesTheOrder()
    {
        var order = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        var result = await ReceiveAsync(order.Id, good: 90m);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        result.MinCompleteQuantity.Should().Be(90m);
        result.MaxAcceptedQuantity.Should().Be(110m);
        var log = await _h.Timers.Db.JobActivityLogs.AsNoTracking().SingleAsync(a => a.FieldName == "Subcontract");
        log.Description.Should().Contain("closed within tolerance");
    }

    [Fact]
    public async Task UnderBelowVendorTolerance_StaysOpen()
    {
        var order = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        var result = await ReceiveAsync(order.Id, good: 89m);

        result.Status.Should().Be(nameof(SubcontractStatus.PartiallyReceived));
    }

    [Fact]
    public async Task OverInsideTolerance_CountsEveryGoodPieceOnTheStep()
    {
        var order = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        var result = await ReceiveAsync(order.Id, good: 110m);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        var row = await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync();
        row.CompletedQuantity.Should().Be(110m);
        row.Status.Should().Be(JobOperationStatus.Complete);
    }

    [Fact]
    public async Task OverAboveTolerance_IsRefusedWithoutAConfirmedManagerOverride()
    {
        var order = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        var unconfirmed = () => ReceiveAsync(order.Id, good: 140m, roles: ["Manager"]);
        (await unconfirmed.Should().ThrowAsync<QuantityToleranceException>())
            .Which.Violation.Should().Match<QuantityToleranceViolationResponseModel>(v => v.CanOverride && v.MaxAccepted == 110m);

        var notAManager = () => ReceiveAsync(order.Id, good: 140m, confirm: true, reason: "Vendor sent extra", roles: ["Engineer"]);
        (await notAManager.Should().ThrowAsync<QuantityToleranceException>()).Which.Violation.CanOverride.Should().BeFalse();

        var noReason = () => ReceiveAsync(order.Id, good: 140m, confirm: true, roles: ["Manager"]);
        await noReason.Should().ThrowAsync<QuantityToleranceException>();

        var after = await OrderAsync(order.Id);
        after.ReceivedQuantity.Should().BeNull();
        after.Status.Should().Be(SubcontractStatus.Sent);
    }

    [Fact]
    public async Task OverAboveTolerance_ManagerConfirmsWithAReason_IsAcceptedAndRecorded()
    {
        var order = await SeedAsync(vendorOver: 10m, vendorUnder: 10m);

        var result = await ReceiveAsync(order.Id, good: 140m, confirm: true, reason: "Yield came in high", roles: ["Manager"]);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        result.OverToleranceReason.Should().Be("Yield came in high");
        (await _h.Timers.Db.JobOperations.AsNoTracking().SingleAsync()).CompletedQuantity.Should().Be(140m);
    }

    [Fact]
    public async Task SubcontractPoLineTolerance_WinsOverTheVendors()
    {
        var order = await SeedAsync(vendorOver: 0m, vendorUnder: 0m, lineOver: 20m, lineUnder: 20m);

        var result = await ReceiveAsync(order.Id, good: 80m);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        result.OverTolerancePct.Should().Be(20m);
    }

    [Fact]
    public async Task CompanyDefault_AppliesWhenTheVendorHasNone()
    {
        _h.Timers.Db.SystemSettings.AddRange(
            new SystemSetting { Key = ToleranceSettings.OverReceiptPercentKey, Value = "5" },
            new SystemSetting { Key = ToleranceSettings.UnderReceiptPercentKey, Value = "5" });
        await _h.Timers.Db.SaveChangesAsync();
        var order = await SeedAsync();

        var result = await ReceiveAsync(order.Id, good: 95m);

        result.Status.Should().Be(nameof(SubcontractStatus.Complete));
        result.MaxAcceptedQuantity.Should().Be(105m);
    }
}
