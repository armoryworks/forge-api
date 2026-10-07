using FluentAssertions;
using Forge.Api.Features.SalesOrders;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class GetAssignableSalesOrderLinesHandlerTests
{
    private readonly AppDbContext _db;
    private readonly GetAssignableSalesOrderLinesHandler _handler;

    public GetAssignableSalesOrderLinesHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new GetAssignableSalesOrderLinesHandler(_db, new SalesOrderAcceptanceGate(_db, StubCapabilitySnapshotProvider.Off));
    }

    private async Task<(int unassignedLineId, int assignedLineId)> SeedAsync()
    {
        var so = new SalesOrder { OrderNumber = "SO-100", CustomerId = 1, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();

        var unassigned = new SalesOrderLine { SalesOrderId = so.Id, Description = "Free line", Quantity = 2m, UnitPrice = 5m, LineNumber = 1 };
        var assigned = new SalesOrderLine { SalesOrderId = so.Id, Description = "Taken line", Quantity = 1m, UnitPrice = 9m, LineNumber = 2 };
        _db.SalesOrderLines.AddRange(unassigned, assigned);
        await _db.SaveChangesAsync();

        // An OPEN job (not archived, not disposed) on the "assigned" line.
        _db.Jobs.Add(new Job { JobNumber = "JOB-1", Title = "Open", TrackTypeId = 1, CurrentStageId = 1, SalesOrderLineId = assigned.Id });
        await _db.SaveChangesAsync();

        return (unassigned.Id, assigned.Id);
    }

    [Fact] // #27 — default returns only lines with no open job.
    public async Task Handle_DefaultsToUnassignedLinesOnly()
    {
        var (unassignedId, assignedId) = await SeedAsync();

        var result = await _handler.Handle(new GetAssignableSalesOrderLinesQuery(false, null), CancellationToken.None);

        result.Select(r => r.Id).Should().Contain(unassignedId);
        result.Select(r => r.Id).Should().NotContain(assignedId, "lines with an open job are hidden by default");
        result.Single(r => r.Id == unassignedId).AssignedJobCount.Should().Be(0);
    }

    [Fact] // #27 — the override surfaces already-assigned lines too, with the open-job count.
    public async Task Handle_IncludeAssigned_SurfacesAssignedLines()
    {
        var (unassignedId, assignedId) = await SeedAsync();

        var result = await _handler.Handle(new GetAssignableSalesOrderLinesQuery(true, null), CancellationToken.None);

        result.Select(r => r.Id).Should().Contain(new[] { unassignedId, assignedId });
        result.Single(r => r.Id == assignedId).AssignedJobCount.Should().Be(1);
    }

    [Fact] // #27 — an archived job does not count as an active assignment.
    public async Task Handle_ArchivedJob_LineStaysUnassigned()
    {
        var so = new SalesOrder { OrderNumber = "SO-200", CustomerId = 1, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        var line = new SalesOrderLine { SalesOrderId = so.Id, Description = "Line", Quantity = 1m, UnitPrice = 1m, LineNumber = 1 };
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();
        _db.Jobs.Add(new Job { JobNumber = "JOB-A", Title = "Archived", TrackTypeId = 1, CurrentStageId = 1, SalesOrderLineId = line.Id, IsArchived = true });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetAssignableSalesOrderLinesQuery(false, null), CancellationToken.None);

        result.Select(r => r.Id).Should().Contain(line.Id, "an archived job is not an active assignment");
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Cancelled)]
    [InlineData(SalesOrderStatus.Shipped)]
    [InlineData(SalesOrderStatus.Completed)]
    public async Task Handle_NonWorkableStatus_LineIsNotAssignable(SalesOrderStatus status)
    {
        var so = new SalesOrder { OrderNumber = $"SO-{status}", CustomerId = 1, Status = status };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        _db.SalesOrderLines.Add(new SalesOrderLine
        { SalesOrderId = so.Id, Description = "Line", Quantity = 1m, UnitPrice = 1m, LineNumber = 1 });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetAssignableSalesOrderLinesQuery(false, null), CancellationToken.None);

        result.Should().BeEmpty($"a {status} order is not workable");
    }

    [Theory]
    [InlineData(SalesOrderStatus.Confirmed)]
    [InlineData(SalesOrderStatus.InProduction)]
    [InlineData(SalesOrderStatus.PartiallyShipped)]
    public async Task Handle_WorkableStatus_LineIsAssignable(SalesOrderStatus status)
    {
        var so = new SalesOrder { OrderNumber = $"SO-{status}", CustomerId = 1, Status = status };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        _db.SalesOrderLines.Add(new SalesOrderLine
        { SalesOrderId = so.Id, Description = "Line", Quantity = 1m, UnitPrice = 1m, LineNumber = 1 });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetAssignableSalesOrderLinesQuery(false, null), CancellationToken.None);

        result.Should().HaveCount(1, $"a {status} order is workable");
    }

    [Fact]
    public async Task Handle_ReportsRemainingQuantityAndRequestedDeliveryDate()
    {
        var requested = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var so = new SalesOrder
        {
            OrderNumber = "SO-300", CustomerId = 1, Status = SalesOrderStatus.PartiallyShipped,
            RequestedDeliveryDate = requested,
        };
        _db.SalesOrders.Add(so);
        await _db.SaveChangesAsync();
        var line = new SalesOrderLine
        {
            SalesOrderId = so.Id, PartId = 900, Description = "Line", Quantity = 100m,
            ShippedQuantity = 10m, UnitPrice = 1m, LineNumber = 1,
        };
        _db.SalesOrderLines.Add(line);
        await _db.SaveChangesAsync();

        Job Linked(string number, decimal qty, bool archived = false, JobDisposition? disposition = null)
        {
            var job = new Job
            {
                JobNumber = number, Title = number, TrackTypeId = 1, CurrentStageId = 1, PartId = 900,
                SalesOrderLineId = line.Id, IsArchived = archived, Disposition = disposition,
            };
            job.JobParts.Add(new JobPart { PartId = 900, Quantity = qty });
            return job;
        }

        var open = Linked("JOB-O", 30m);
        open.JobParts.Add(new JobPart { PartId = 901, Quantity = 500m });
        _db.Jobs.AddRange(
            open,
            Linked("JOB-A", 15m, archived: true),
            Linked("JOB-D", 20m, disposition: JobDisposition.Scrap));
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetAssignableSalesOrderLinesQuery(true, null), CancellationToken.None);

        var row = result.Single(r => r.Id == line.Id);
        row.RemainingQuantity.Should().Be(60m);
        row.RequestedDeliveryDate.Should().Be(requested);
    }
}
