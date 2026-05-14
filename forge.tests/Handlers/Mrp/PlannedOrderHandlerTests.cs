using FluentAssertions;

using Forge.Api.Features.Mrp;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Mrp;

public class PlannedOrderHandlerTests
{
    [Fact]
    public async Task GetPlannedOrders_ReturnsAll()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "PO-001", Description = "Order Part", Status = PartStatus.Active };
        db.Parts.Add(part);
        var run = new MrpRun { RunNumber = "MRP-TEST", RunType = MrpRunType.Full, Status = MrpRunStatus.Completed, PlanningHorizonDays = 90 };
        db.MrpRuns.Add(run);
        await db.SaveChangesAsync();

        db.MrpPlannedOrders.Add(new MrpPlannedOrder
        {
            MrpRunId = run.Id,
            PartId = part.Id,
            OrderType = MrpOrderType.Purchase,
            Status = MrpPlannedOrderStatus.Planned,
            Quantity = 100m,
            StartDate = DateTimeOffset.UtcNow,
            DueDate = DateTimeOffset.UtcNow.AddDays(14),
        });
        await db.SaveChangesAsync();

        var handler = new GetPlannedOrdersHandler(db);

        // Act
        var result = await handler.Handle(new GetPlannedOrdersQuery(null, null), CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].PartNumber.Should().Be("PO-001");
    }

    [Fact]
    public async Task UpdatePlannedOrder_FirmsOrder()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var run = new MrpRun { RunNumber = "MRP-FIRM", RunType = MrpRunType.Full, Status = MrpRunStatus.Completed, PlanningHorizonDays = 90 };
        db.MrpRuns.Add(run);
        var part = new Part { PartNumber = "FIRM-001", Description = "Firm Part", Status = PartStatus.Active };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var order = new MrpPlannedOrder
        {
            MrpRunId = run.Id,
            PartId = part.Id,
            OrderType = MrpOrderType.Purchase,
            Status = MrpPlannedOrderStatus.Planned,
            Quantity = 50m,
            StartDate = DateTimeOffset.UtcNow,
            DueDate = DateTimeOffset.UtcNow.AddDays(14),
        };
        db.MrpPlannedOrders.Add(order);
        await db.SaveChangesAsync();

        var handler = new UpdatePlannedOrderHandler(db);

        // Act
        await handler.Handle(new UpdatePlannedOrderCommand(order.Id, true, "Firmed for Q2"), CancellationToken.None);

        // Assert
        var updated = await db.MrpPlannedOrders.FindAsync(order.Id);
        updated!.IsFirmed.Should().BeTrue();
        updated.Status.Should().Be(MrpPlannedOrderStatus.Firmed);
        updated.Notes.Should().Be("Firmed for Q2");
    }

    [Fact]
    public async Task DeletePlannedOrder_SetsCancelled()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var run = new MrpRun { RunNumber = "MRP-DEL", RunType = MrpRunType.Full, Status = MrpRunStatus.Completed, PlanningHorizonDays = 90 };
        db.MrpRuns.Add(run);
        var part = new Part { PartNumber = "DEL-001", Description = "Delete Part", Status = PartStatus.Active };
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        var order = new MrpPlannedOrder
        {
            MrpRunId = run.Id,
            PartId = part.Id,
            OrderType = MrpOrderType.Purchase,
            Status = MrpPlannedOrderStatus.Planned,
            Quantity = 25m,
            StartDate = DateTimeOffset.UtcNow,
            DueDate = DateTimeOffset.UtcNow.AddDays(14),
        };
        db.MrpPlannedOrders.Add(order);
        await db.SaveChangesAsync();

        var handler = new DeletePlannedOrderHandler(db);

        // Act
        await handler.Handle(new DeletePlannedOrderCommand(order.Id), CancellationToken.None);

        // Assert
        var updated = await db.MrpPlannedOrders.FindAsync(order.Id);
        updated!.Status.Should().Be(MrpPlannedOrderStatus.Cancelled);
    }
}
