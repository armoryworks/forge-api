using FluentAssertions;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class GetPartQualitySummaryTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<Part> SeedPartAsync(int id, string partNumber, int? receivingTemplateId = null)
    {
        var part = new Part
        {
            Id = id, PartNumber = partNumber, Description = partNumber,
            ReceivingInspectionTemplateId = receivingTemplateId,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private Task<PartQualitySummaryResponseModel> SummaryAsync(int partId) =>
        new GetPartQualitySummaryHandler(_db).Handle(new GetPartQualitySummaryQuery(partId), CancellationToken.None);

    [Fact]
    public async Task Unknown_part_throws_not_found()
    {
        var act = () => SummaryAsync(999);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Part_with_an_inspection_and_an_open_ncr_shows_both()
    {
        var part = await SeedPartAsync(11, "QS-PART");
        _db.LotRecords.Add(new LotRecord { Id = 21, LotNumber = "QS-LOT-1", PartId = part.Id, Quantity = 5m });
        _db.QcInspections.Add(new QcInspection { Id = 31, LotNumber = "QS-LOT-1", Status = "Failed", InspectorId = 1 });
        _db.QcInspectionResults.AddRange(
            new QcInspectionResult { InspectionId = 31, Description = "Bore", Passed = false },
            new QcInspectionResult { InspectionId = 31, Description = "Finish", Passed = true });
        _db.NonConformances.AddRange(
            new NonConformance
            {
                Id = 41, NcrNumber = "NCR-QS-1", PartId = part.Id, LotNumber = "QS-LOT-1",
                Type = NcrType.Internal, Status = NcrStatus.Open, Description = "Bore oversize", AffectedQuantity = 2m,
            },
            new NonConformance
            {
                Id = 42, NcrNumber = "NCR-QS-2", PartId = part.Id,
                Type = NcrType.Internal, Status = NcrStatus.Closed, Description = "Closed one",
            });
        await _db.SaveChangesAsync();

        var result = await SummaryAsync(part.Id);

        var inspection = result.RecentInspections.Should().ContainSingle().Subject;
        inspection.Id.Should().Be(31);
        inspection.Status.Should().Be("Failed");
        inspection.LotNumber.Should().Be("QS-LOT-1");
        inspection.PassedCount.Should().Be(1);
        inspection.FailedCount.Should().Be(1);
        var ncr = result.OpenNcrs.Should().ContainSingle().Subject;
        ncr.NcrNumber.Should().Be("NCR-QS-1");
        ncr.Status.Should().Be("Open");
    }

    [Fact]
    public async Task An_unchecked_result_counts_as_neither_passed_nor_failed()
    {
        var part = await SeedPartAsync(12, "QS-UNCHECKED");
        _db.LotRecords.Add(new LotRecord { Id = 22, LotNumber = "QS-LOT-2", PartId = part.Id, Quantity = 5m });
        _db.QcInspections.Add(new QcInspection { Id = 32, LotNumber = "QS-LOT-2", Status = "InProgress", InspectorId = 1 });
        _db.QcInspectionResults.AddRange(
            new QcInspectionResult { InspectionId = 32, Description = "Bore", Passed = true },
            new QcInspectionResult { InspectionId = 32, Description = "Finish", Passed = false },
            new QcInspectionResult { InspectionId = 32, Description = "Thread" });
        await _db.SaveChangesAsync();

        var inspection = (await SummaryAsync(part.Id)).RecentInspections.Should().ContainSingle().Subject;

        inspection.PassedCount.Should().Be(1);
        inspection.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task Inspections_are_matched_by_template_job_run_and_receipt_and_capped_at_ten()
    {
        var part = await SeedPartAsync(12, "QS-MULTI");
        var other = await SeedPartAsync(13, "QS-OTHER");
        _db.TrackTypes.Add(new TrackType { Id = 5, Name = "Prod", Code = "prod", IsActive = true });
        _db.JobStages.Add(new JobStage { Id = 51, TrackTypeId = 5, Name = "S1", Code = "s1", SortOrder = 1 });
        _db.Jobs.Add(new Job { Id = 61, JobNumber = "J-QS", Title = "QS", TrackTypeId = 5, CurrentStageId = 51, PartId = part.Id });
        _db.Jobs.Add(new Job { Id = 62, JobNumber = "J-OTHER", Title = "Other", TrackTypeId = 5, CurrentStageId = 51, PartId = other.Id });
        _db.QcChecklistTemplates.Add(new QcChecklistTemplate { Id = 71, Name = "QS template", PartId = part.Id });
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 12; i++)
        {
            _db.QcInspections.Add(new QcInspection
            {
                Id = 100 + i, JobId = 61, Status = "Passed", InspectorId = 1, CreatedAt = start.AddDays(i),
            });
        }
        _db.QcInspections.Add(new QcInspection { Id = 200, JobId = 62, Status = "Passed", InspectorId = 1, CreatedAt = start.AddDays(30) });
        await _db.SaveChangesAsync();

        var result = await SummaryAsync(part.Id);

        result.RecentInspections.Should().HaveCount(10);
        result.RecentInspections.Should().NotContain(i => i.Id == 200);
        result.RecentInspections.Should().BeInDescendingOrder(i => i.CreatedAt);
        result.RecentInspections.Should().AllSatisfy(i => i.JobNumber.Should().Be("J-QS"));
    }

    [Fact]
    public async Task Receipt_inspection_without_lot_or_job_is_attributed_to_the_part()
    {
        var part = await SeedPartAsync(14, "QS-RECV");
        var vendor = new Vendor { CompanyName = "Supplier" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-QS", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        var line = new PurchaseOrderLine { PurchaseOrderId = po.Id, PartId = part.Id, Description = "x", OrderedQuantity = 1, UnitPrice = 1m };
        _db.PurchaseOrderLines.Add(line);
        _db.QcInspections.Add(new QcInspection { Id = 81, Status = "Passed", InspectorId = 1 });
        await _db.SaveChangesAsync();
        _db.ReceivingRecords.Add(new ReceivingRecord { PurchaseOrderLineId = line.Id, QuantityReceived = 1, QcInspectionId = 81 });
        await _db.SaveChangesAsync();

        var result = await SummaryAsync(part.Id);

        result.RecentInspections.Should().ContainSingle(i => i.Id == 81);
    }

    [Fact]
    public async Task Lots_report_on_hand_and_hold_status()
    {
        var part = await SeedPartAsync(15, "QS-LOTS");
        _db.LotRecords.AddRange(
            new LotRecord { Id = 91, LotNumber = "QS-HELD", PartId = part.Id, Quantity = 10m },
            new LotRecord { Id = 92, LotNumber = "QS-FREE", PartId = part.Id, Quantity = 4m },
            new LotRecord { Id = 93, LotNumber = "QS-EMPTY", PartId = part.Id, Quantity = 1m });
        _db.BinContents.AddRange(
            new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, LotNumber = "QS-HELD", Quantity = 6m, Status = BinContentStatus.Stored },
            new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, LotNumber = "QS-HELD", Quantity = 4m, Status = BinContentStatus.QcHold },
            new BinContent { LocationId = 1, EntityType = "part", EntityId = part.Id, LotNumber = "QS-FREE", Quantity = 4m, Status = BinContentStatus.Stored },
            new BinContent
            {
                LocationId = 1, EntityType = "part", EntityId = part.Id, LotNumber = "QS-EMPTY", Quantity = 1m,
                Status = BinContentStatus.Stored, RemovedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            });
        await _db.SaveChangesAsync();

        var result = await SummaryAsync(part.Id);

        result.Lots.Select(l => l.LotNumber).Should().BeEquivalentTo(["QS-HELD", "QS-FREE"]);
        var held = result.Lots.Single(l => l.LotNumber == "QS-HELD");
        held.OnHandQuantity.Should().Be(10m);
        held.HeldQuantity.Should().Be(4m);
        held.IsOnHold.Should().BeTrue();
        var free = result.Lots.Single(l => l.LotNumber == "QS-FREE");
        free.IsOnHold.Should().BeFalse();
        free.HeldQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task Templates_include_part_templates_and_the_receiving_template_and_spc_count_is_active_only()
    {
        _db.QcChecklistTemplates.AddRange(
            new QcChecklistTemplate { Id = 301, Name = "Generic receiving" },
            new QcChecklistTemplate { Id = 302, Name = "Part final", PartId = 16 },
            new QcChecklistTemplate { Id = 303, Name = "Unrelated" });
        await _db.SaveChangesAsync();
        var part = await SeedPartAsync(16, "QS-TPL", receivingTemplateId: 301);
        _db.SpcCharacteristics.AddRange(
            new SpcCharacteristic { PartId = part.Id, Name = "OD", IsActive = true },
            new SpcCharacteristic { PartId = part.Id, Name = "ID", IsActive = true },
            new SpcCharacteristic { PartId = part.Id, Name = "Old", IsActive = false });
        await _db.SaveChangesAsync();

        var result = await SummaryAsync(part.Id);

        result.InspectionTemplates.Select(t => t.Id).Should().BeEquivalentTo([301, 302]);
        result.InspectionTemplates.Single(t => t.Id == 301).IsReceivingTemplate.Should().BeTrue();
        result.InspectionTemplates.Single(t => t.Id == 302).IsReceivingTemplate.Should().BeFalse();
        result.SpcCharacteristicCount.Should().Be(2);
    }
}
