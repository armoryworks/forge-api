using FluentAssertions;

using Forge.Api.Features.Lots;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Lots;

public class GetLotTraceabilitySourcesTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<Part> SeedPartAsync(int id, string partNumber)
    {
        var part = new Part { Id = id, PartNumber = partNumber, Description = partNumber };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<Job> SeedJobAsync(int id, string jobNumber, int? partId = null)
    {
        if (!_db.TrackTypes.Any())
        {
            _db.TrackTypes.Add(new TrackType { Id = 5, Name = "Prod", Code = "prod", IsActive = true });
            _db.JobStages.Add(new JobStage { Id = 51, TrackTypeId = 5, Name = "S1", Code = "s1", SortOrder = 1 });
        }
        var job = new Job { Id = id, JobNumber = jobNumber, Title = $"Make {jobNumber}", TrackTypeId = 5, CurrentStageId = 51, PartId = partId };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private async Task<ReceivingRecord> SeedReceiptAsync(Part part, string lotNumber, decimal quantity)
    {
        var vendor = new Vendor { CompanyName = "Steel Supply" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        var po = new PurchaseOrder { PONumber = "PO-2001", VendorId = vendor.Id, Status = PurchaseOrderStatus.Submitted };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        var line = new PurchaseOrderLine
        {
            PurchaseOrderId = po.Id, PartId = part.Id, Description = part.PartNumber,
            OrderedQuantity = quantity, UnitPrice = 2m,
        };
        _db.PurchaseOrderLines.Add(line);
        await _db.SaveChangesAsync();
        var record = new ReceivingRecord
        {
            PurchaseOrderLineId = line.Id,
            QuantityReceived = quantity,
            LotNumber = lotNumber,
            ReceiptNumber = "R-20261008-0001",
            InspectionStatus = ReceivingInspectionStatus.Passed,
            CreatedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        };
        _db.ReceivingRecords.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    private Task<LotTraceabilityResponseModel> TraceAsync(string lotNumber) =>
        new GetLotTraceabilityHandler(_db).Handle(new GetLotTraceabilityQuery(lotNumber), CancellationToken.None);

    [Fact]
    public async Task Received_lot_names_its_purchase_order_vendor_date_and_quantity()
    {
        var part = await SeedPartAsync(101, "BAR-STOCK");
        _db.LotRecords.Add(new LotRecord { Id = 201, LotNumber = "LOT-RCV-1", PartId = part.Id, Quantity = 40m });
        await _db.SaveChangesAsync();
        var receipt = await SeedReceiptAsync(part, "LOT-RCV-1", 40m);

        var result = await TraceAsync("LOT-RCV-1");

        var source = result.ReceivedFrom.Should().ContainSingle().Subject;
        source.ReceivingRecordId.Should().Be(receipt.Id);
        source.PoNumber.Should().Be("PO-2001");
        source.VendorName.Should().Be("Steel Supply");
        source.ReceivedAt.Should().Be(receipt.CreatedAt);
        source.Quantity.Should().Be(40m);
        source.ReceiptNumber.Should().Be("R-20261008-0001");
        source.InspectionStatus.Should().Be("Passed");
        result.ProducingJob.Should().BeNull();
    }

    [Fact]
    public async Task Inspections_match_the_lot_and_part_and_include_the_receipt_inspection()
    {
        var part = await SeedPartAsync(102, "PLATE");
        var otherPart = await SeedPartAsync(103, "OTHER");
        var otherJob = await SeedJobAsync(301, "J-OTHER", otherPart.Id);
        _db.LotRecords.Add(new LotRecord { Id = 202, LotNumber = "LOT-INS-1", PartId = part.Id, Quantity = 10m });
        var template = new QcChecklistTemplate { Id = 401, Name = "Plate receiving", PartId = part.Id };
        _db.QcChecklistTemplates.Add(template);
        _db.QcInspections.AddRange(
            new QcInspection { Id = 501, LotNumber = "LOT-INS-1", TemplateId = template.Id, Status = "Passed", InspectorId = 1 },
            new QcInspection { Id = 502, LotNumber = "LOT-INS-1", JobId = otherJob.Id, Status = "Failed", InspectorId = 1 },
            new QcInspection { Id = 503, Status = "Passed", InspectorId = 1 });
        _db.QcInspectionResults.AddRange(
            new QcInspectionResult { InspectionId = 501, Description = "Thickness", Passed = true },
            new QcInspectionResult { InspectionId = 501, Description = "Flatness", Passed = false });
        await _db.SaveChangesAsync();
        var receipt = await SeedReceiptAsync(part, "LOT-INS-1", 10m);
        receipt.QcInspectionId = 503;
        await _db.SaveChangesAsync();

        var result = await TraceAsync("LOT-INS-1");

        result.Inspections.Select(i => i.Id).Should().BeEquivalentTo([501, 503]);
        var templated = result.Inspections.Single(i => i.Id == 501);
        templated.TemplateName.Should().Be("Plate receiving");
        templated.PassedCount.Should().Be(1);
        templated.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task An_unchecked_result_counts_as_neither_passed_nor_failed()
    {
        var part = await SeedPartAsync(105, "BRACKET");
        _db.LotRecords.Add(new LotRecord { Id = 204, LotNumber = "LOT-UNCHECKED-1", PartId = part.Id, Quantity = 5m });
        _db.QcInspections.Add(new QcInspection { Id = 504, LotNumber = "LOT-UNCHECKED-1", Status = "InProgress", InspectorId = 1 });
        _db.QcInspectionResults.AddRange(
            new QcInspectionResult { InspectionId = 504, Description = "Thickness", Passed = true },
            new QcInspectionResult { InspectionId = 504, Description = "Flatness" },
            new QcInspectionResult { InspectionId = 504, Description = "Finish" });
        await _db.SaveChangesAsync();

        var inspection = (await TraceAsync("LOT-UNCHECKED-1")).Inspections.Should().ContainSingle().Subject;

        inspection.PassedCount.Should().Be(1);
        inspection.FailedCount.Should().Be(0);
    }

    [Fact]
    public async Task Non_conformances_raised_for_the_lot_are_listed()
    {
        var part = await SeedPartAsync(104, "SHAFT");
        _db.LotRecords.Add(new LotRecord { Id = 203, LotNumber = "LOT-NCR-1", PartId = part.Id, Quantity = 5m });
        _db.NonConformances.AddRange(
            new NonConformance
            {
                Id = 601, NcrNumber = "NCR-0001", PartId = part.Id, LotNumber = "LOT-NCR-1",
                Type = NcrType.Supplier, Status = NcrStatus.Open, Description = "Out of round",
                AffectedQuantity = 2m, DetectedAt = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero),
            },
            new NonConformance
            {
                Id = 602, NcrNumber = "NCR-0002", PartId = part.Id, LotNumber = "LOT-NCR-OTHER",
                Type = NcrType.Internal, Status = NcrStatus.Open, Description = "Other lot",
            });
        await _db.SaveChangesAsync();

        var result = await TraceAsync("LOT-NCR-1");

        var ncr = result.NonConformances.Should().ContainSingle().Subject;
        ncr.NcrNumber.Should().Be("NCR-0001");
        ncr.Type.Should().Be("Supplier");
        ncr.Status.Should().Be("Open");
        ncr.AffectedQuantity.Should().Be(2m);
        ncr.DispositionCode.Should().BeNull();
    }

    [Fact]
    public async Task Producing_job_comes_from_the_lot_job_first()
    {
        var part = await SeedPartAsync(105, "ASM");
        var job = await SeedJobAsync(302, "J-DIRECT", part.Id);
        _db.LotRecords.Add(new LotRecord { Id = 204, LotNumber = "LOT-JOB-1", PartId = part.Id, JobId = job.Id, Quantity = 1m });
        await _db.SaveChangesAsync();

        var result = await TraceAsync("LOT-JOB-1");

        result.ProducingJob.Should().NotBeNull();
        result.ProducingJob!.JobNumber.Should().Be("J-DIRECT");
    }

    [Fact]
    public async Task Producing_job_falls_back_to_the_production_run_job()
    {
        var part = await SeedPartAsync(106, "ASM-RUN");
        var job = await SeedJobAsync(303, "J-RUN", part.Id);
        _db.ProductionRuns.Add(new ProductionRun { Id = 701, JobId = job.Id, PartId = part.Id, RunNumber = "RUN-1" });
        _db.LotRecords.Add(new LotRecord { Id = 205, LotNumber = "LOT-RUN-1", PartId = part.Id, ProductionRunId = 701, Quantity = 1m });
        await _db.SaveChangesAsync();

        var result = await TraceAsync("LOT-RUN-1");

        result.ProducingJob!.JobNumber.Should().Be("J-RUN");
    }

    [Fact]
    public async Task Producing_job_falls_back_to_the_consumption_producer()
    {
        var part = await SeedPartAsync(107, "ASM-CONS");
        var input = await SeedPartAsync(108, "INPUT");
        var job = await SeedJobAsync(304, "J-CONS", part.Id);
        _db.LotRecords.AddRange(
            new LotRecord { Id = 206, LotNumber = "LOT-CONS-OUT", PartId = part.Id, Quantity = 1m },
            new LotRecord { Id = 207, LotNumber = "LOT-CONS-IN", PartId = input.Id, Quantity = 3m });
        _db.LotConsumptions.Add(new LotConsumption { ConsumedLotId = 207, ProducedLotId = 206, Quantity = 3m, JobId = job.Id });
        await _db.SaveChangesAsync();

        var result = await TraceAsync("LOT-CONS-OUT");

        result.ProducingJob!.JobNumber.Should().Be("J-CONS");
    }
}
