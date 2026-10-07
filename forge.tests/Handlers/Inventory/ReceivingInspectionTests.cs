using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Inventory;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class ReceivingInspectionTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private const int InspectorId = 42;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly FixedClock _clock = new();
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor = new();

    public ReceivingInspectionTests()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, InspectorId.ToString())], "Test");
        _httpContextAccessor.Setup(a => a.HttpContext)
            .Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
    }

    private RecordInspectionResultHandler RecordHandler()
        => new(_db, _httpContextAccessor.Object, new NcrCapaService(_db, _clock), _clock);

    private WaiveInspectionHandler WaiveHandler()
        => new(_db, _httpContextAccessor.Object, _clock);

    private async Task<ReceivingRecord> SeedReceiptAsync(
        decimal quantity = 10m,
        ReceivingInspectionStatus status = ReceivingInspectionStatus.Pending,
        bool withPart = true)
    {
        var vendor = new Vendor { CompanyName = "Supplier" };
        _db.Vendors.Add(vendor);
        Part? part = null;
        if (withPart)
        {
            part = new Part { PartNumber = $"P-{Guid.NewGuid():N}", Description = "Bracket" };
            _db.Parts.Add(part);
        }
        await _db.SaveChangesAsync();

        var po = new PurchaseOrder { PONumber = "PO-1001", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id,
            PartId = part?.Id,
            Description = "Bracket",
            OrderedQuantity = quantity,
            UnitPrice = 5m,
        };
        _db.PurchaseOrderLines.Add(line);
        await _db.SaveChangesAsync();

        var record = new ReceivingRecord
        {
            PurchaseOrderLineId = line.Id,
            QuantityReceived = quantity,
            ReceiptNumber = "R-20261007-0001",
            InspectionStatus = status,
        };
        _db.ReceivingRecords.Add(record);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return record;
    }

    private static RecordInspectionResultCommand Command(
        int id, string result, decimal? accepted, decimal? rejected, bool createNcr = true, string? notes = null)
        => new(id, new InspectionResultRequestModel(result, accepted, rejected, notes, createNcr));

    [Fact]
    public async Task Record_PartialAccept_WithRejection_RaisesSupplierNcrLinkedToReceipt()
    {
        var receipt = await SeedReceiptAsync();

        await RecordHandler().Handle(Command(receipt.Id, "PartialAccept", 8m, 2m, notes: "Burrs on 2"), CancellationToken.None);

        var record = await _db.ReceivingRecords.Include(r => r.PurchaseOrderLine).ThenInclude(l => l.PurchaseOrder)
            .SingleAsync(r => r.Id == receipt.Id);
        record.InspectionStatus.Should().Be(ReceivingInspectionStatus.PartialAccept);
        record.InspectedQuantityAccepted.Should().Be(8m);
        record.InspectedQuantityRejected.Should().Be(2m);
        record.InspectedById.Should().Be(InspectorId);
        record.InspectedAt.Should().Be(_clock.UtcNow);
        record.InspectionNotes.Should().Be("Burrs on 2");

        var ncr = await _db.NonConformances.SingleAsync();
        ncr.Type.Should().Be(NcrType.Supplier);
        ncr.DetectedAtStage.Should().Be(NcrDetectionStage.Receiving);
        ncr.AffectedQuantity.Should().Be(2m);
        ncr.DefectiveQuantity.Should().Be(2m);
        ncr.PartId.Should().Be(record.PurchaseOrderLine.PartId!.Value);
        ncr.PurchaseOrderLineId.Should().Be(record.PurchaseOrderLineId);
        ncr.VendorId.Should().Be(record.PurchaseOrderLine.PurchaseOrder.VendorId);
        ncr.DetectedById.Should().Be(InspectorId);
        ncr.Status.Should().Be(NcrStatus.Open);
        ncr.NcrNumber.Should().Be("NCR-20261007-001");
        ncr.Description.Should().Contain("R-20261007-0001").And.Contain("PO-1001");

        var inspection = await _db.ReceivingInspections.SingleAsync();
        inspection.ReceivingRecordId.Should().Be(receipt.Id);
        inspection.NcrId.Should().Be(ncr.Id);
        inspection.Result.Should().Be(ReceivingInspectionResult.ConditionalAccept);
        inspection.AcceptedQuantity.Should().Be(8m);
        inspection.RejectedQuantity.Should().Be(2m);

        var logs = await _db.ActivityLogs.ToListAsync();
        logs.Should().ContainSingle(l => l.EntityType == "PurchaseOrder" && l.Action == "inspection-recorded"
            && l.EntityId == record.PurchaseOrderLine.PurchaseOrderId);
        logs.Should().ContainSingle(l => l.EntityType == "NonConformance" && l.EntityId == ncr.Id && l.Action == "created");
    }

    [Fact]
    public async Task Record_Failed_WithCreateNcrOff_DoesNotRaiseNcr()
    {
        var receipt = await SeedReceiptAsync();

        await RecordHandler().Handle(Command(receipt.Id, "Failed", 0m, 10m, createNcr: false), CancellationToken.None);

        (await _db.ReceivingRecords.SingleAsync()).InspectionStatus.Should().Be(ReceivingInspectionStatus.Failed);
        (await _db.NonConformances.CountAsync()).Should().Be(0);
        (await _db.ReceivingInspections.SingleAsync()).NcrId.Should().BeNull();
    }

    [Fact]
    public async Task Record_Passed_WithNothingRejected_DoesNotRaiseNcr()
    {
        var receipt = await SeedReceiptAsync();

        await RecordHandler().Handle(Command(receipt.Id, "Passed", 10m, 0m), CancellationToken.None);

        (await _db.ReceivingRecords.SingleAsync()).InspectionStatus.Should().Be(ReceivingInspectionStatus.Passed);
        (await _db.NonConformances.CountAsync()).Should().Be(0);
        (await _db.ReceivingInspections.SingleAsync()).Result.Should().Be(ReceivingInspectionResult.Accept);
    }

    [Fact]
    public async Task Record_FromInProgress_IsAllowed()
    {
        var receipt = await SeedReceiptAsync(status: ReceivingInspectionStatus.InProgress);

        await RecordHandler().Handle(Command(receipt.Id, "Passed", 10m, 0m), CancellationToken.None);

        (await _db.ReceivingRecords.SingleAsync()).InspectionStatus.Should().Be(ReceivingInspectionStatus.Passed);
    }

    [Theory]
    [InlineData(ReceivingInspectionStatus.NotRequired)]
    [InlineData(ReceivingInspectionStatus.Passed)]
    [InlineData(ReceivingInspectionStatus.Failed)]
    [InlineData(ReceivingInspectionStatus.Waived)]
    [InlineData(ReceivingInspectionStatus.PartialAccept)]
    public async Task Record_FromClosedStatus_Throws(ReceivingInspectionStatus status)
    {
        var receipt = await SeedReceiptAsync(status: status);

        var act = () => RecordHandler().Handle(Command(receipt.Id, "Passed", 10m, 0m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.ReceivingRecords.SingleAsync()).InspectionStatus.Should().Be(status);
        (await _db.ReceivingInspections.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 3)]
    public async Task Record_QuantitiesNotSummingToReceived_Throws(decimal accepted, decimal rejected)
    {
        var receipt = await SeedReceiptAsync();

        var act = () => RecordHandler().Handle(Command(receipt.Id, "PartialAccept", accepted, rejected), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        (await _db.ReceivingRecords.SingleAsync()).InspectionStatus.Should().Be(ReceivingInspectionStatus.Pending);
        (await _db.NonConformances.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("Waived")]
    [InlineData("Pending")]
    [InlineData("Bogus")]
    public async Task Record_ResultOutsideAllowedSet_Throws(string result)
    {
        var receipt = await SeedReceiptAsync();

        var act = () => RecordHandler().Handle(Command(receipt.Id, result, 10m, 0m), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Record_RejectionOnLineWithoutPart_WithCreateNcr_Throws()
    {
        var receipt = await SeedReceiptAsync(withPart: false);

        var act = () => RecordHandler().Handle(Command(receipt.Id, "Failed", 0m, 10m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.NonConformances.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Record_UnknownReceipt_ThrowsNotFound()
    {
        var act = () => RecordHandler().Handle(Command(999, "Passed", 1m, 0m), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData("Passed", 10, 0, true)]
    [InlineData("Failed", 0, 10, true)]
    [InlineData("PartialAccept", 8, 2, true)]
    [InlineData("PartialAccept", 10, 0, false)]
    [InlineData("PartialAccept", 0, 10, false)]
    [InlineData("Waived", 10, 0, false)]
    [InlineData("InProgress", 10, 0, false)]
    [InlineData("Passed", -1, 11, false)]
    [InlineData("Passed", 8, 2, false)]
    [InlineData("Failed", 3, 7, false)]
    public void RecordValidator_EnforcesResultAndQuantityRules(string result, decimal accepted, decimal rejected, bool valid)
    {
        var outcome = new RecordInspectionResultValidator().Validate(Command(1, result, accepted, rejected));

        outcome.IsValid.Should().Be(valid);
    }

    [Fact]
    public void RecordValidator_RequiresBothQuantities()
    {
        var outcome = new RecordInspectionResultValidator().Validate(Command(1, "Passed", null, null));

        outcome.IsValid.Should().BeFalse();
        outcome.Errors.Select(e => e.PropertyName).Should().Contain(["Data.AcceptedQuantity", "Data.RejectedQuantity"]);
    }

    [Fact]
    public async Task Waive_FromPending_StoresReasonAndLogs()
    {
        var receipt = await SeedReceiptAsync();

        await WaiveHandler().Handle(new WaiveInspectionCommand(receipt.Id, "  Certified supplier lot  "), CancellationToken.None);

        var record = await _db.ReceivingRecords.SingleAsync();
        record.InspectionStatus.Should().Be(ReceivingInspectionStatus.Waived);
        record.InspectionNotes.Should().Be("Certified supplier lot");
        record.InspectedById.Should().Be(InspectorId);
        record.InspectedAt.Should().Be(_clock.UtcNow);
        (await _db.ActivityLogs.Where(l => l.Action == "inspection-waived").SingleAsync())
            .Description.Should().Contain("Certified supplier lot");
    }

    [Theory]
    [InlineData(ReceivingInspectionStatus.Failed)]
    [InlineData(ReceivingInspectionStatus.Passed)]
    [InlineData(ReceivingInspectionStatus.Waived)]
    [InlineData(ReceivingInspectionStatus.NotRequired)]
    public async Task Waive_FromClosedStatus_Throws(ReceivingInspectionStatus status)
    {
        var receipt = await SeedReceiptAsync(status: status);

        var act = () => WaiveHandler().Handle(new WaiveInspectionCommand(receipt.Id, "Reason"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _db.ReceivingRecords.SingleAsync()).InspectionStatus.Should().Be(status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WaiveValidator_RequiresReason(string reason)
    {
        new WaiveInspectionValidator().Validate(new WaiveInspectionCommand(1, reason)).IsValid.Should().BeFalse();
    }
}
