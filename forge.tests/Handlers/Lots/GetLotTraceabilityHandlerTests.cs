using FluentAssertions;

using Forge.Api.Features.Lots;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Lots;

/// <summary>
/// QA regression — the lot detail panel binds quantity/expiration/supplier and a
/// flattened events timeline, but the trace endpoint returned none of them: the
/// quantity rendered blank and the timeline died on the missing events array.
/// These pin the panel's actual contract.
/// </summary>
public class GetLotTraceabilityHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<LotRecord> SeedLotWithJobAsync()
    {
        var part = new Part { Id = 11, PartNumber = "LOT-PART-1", Description = "Lot part" };
        _db.Parts.Add(part);

        var track = new TrackType { Id = 5, Name = "Prod", Code = "prod", IsActive = true };
        _db.TrackTypes.Add(track);
        var stage = new JobStage { Id = 51, TrackTypeId = 5, Name = "S1", Code = "s1", SortOrder = 1 };
        _db.JobStages.Add(stage);
        var job = new Job { Id = 21, JobNumber = "J-5", Title = "Build widgets", TrackTypeId = 5, CurrentStageId = 51 };
        _db.Jobs.Add(job);

        var lot = new LotRecord
        {
            Id = 31,
            LotNumber = "LOT-20260707-001",
            PartId = part.Id,
            JobId = job.Id,
            Quantity = 25.5m,
            ExpirationDate = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            SupplierLotNumber = "SUP-9",
        };
        _db.LotRecords.Add(lot);
        await _db.SaveChangesAsync();
        return lot;
    }

    [Fact]
    public async Task Trace_returns_lot_header_fields_the_panel_binds()
    {
        var lot = await SeedLotWithJobAsync();

        var result = await new GetLotTraceabilityHandler(_db)
            .Handle(new GetLotTraceabilityQuery(lot.LotNumber), CancellationToken.None);

        result.Quantity.Should().Be(25.5m, "the panel's quantity field rendered blank without this");
        result.ExpirationDate.Should().Be(lot.ExpirationDate);
        result.SupplierLotNumber.Should().Be("SUP-9");
        result.PartNumber.Should().Be("LOT-PART-1");
    }

    [Fact]
    public async Task Trace_returns_flattened_job_event_with_lot_quantity()
    {
        var lot = await SeedLotWithJobAsync();

        var result = await new GetLotTraceabilityHandler(_db)
            .Handle(new GetLotTraceabilityQuery(lot.LotNumber), CancellationToken.None);

        result.Events.Should().ContainSingle(e => e.Type == "Job");
        var jobEvent = result.Events.Single(e => e.Type == "Job");
        jobEvent.ReferenceNumber.Should().Be("J-5");
        jobEvent.Description.Should().Be("Build widgets");
        jobEvent.Quantity.Should().Be(25.5m);
    }

    [Fact]
    public async Task Trace_with_no_linked_records_returns_empty_events_not_null()
    {
        var part = new Part { Id = 12, PartNumber = "LOT-PART-2" };
        _db.Parts.Add(part);
        _db.LotRecords.Add(new LotRecord { Id = 32, LotNumber = "LOT-BARE", PartId = 12, Quantity = 1m });
        await _db.SaveChangesAsync();

        var result = await new GetLotTraceabilityHandler(_db)
            .Handle(new GetLotTraceabilityQuery("LOT-BARE"), CancellationToken.None);

        result.Events.Should().NotBeNull().And.BeEmpty("the timeline template iterates events unconditionally");
    }

    [Fact]
    public async Task Events_are_ordered_by_date_ascending()
    {
        var lot = await SeedLotWithJobAsync();
        _db.QcInspections.Add(new QcInspection
        {
            Id = 41, LotNumber = lot.LotNumber, Status = "Passed", InspectorId = 999,
        });
        await _db.SaveChangesAsync();

        var result = await new GetLotTraceabilityHandler(_db)
            .Handle(new GetLotTraceabilityQuery(lot.LotNumber), CancellationToken.None);

        result.Events.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Events.Should().BeInAscendingOrder(e => e.Date);
    }
}
