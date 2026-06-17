using FluentAssertions;
using Forge.Api.Features.SalesOrders;
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
        _handler = new GetAssignableSalesOrderLinesHandler(_db);
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
}
