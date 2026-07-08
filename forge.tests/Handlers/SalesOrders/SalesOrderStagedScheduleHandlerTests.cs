using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

/// <summary>
/// S4c — staged production/shipment/payment scheduling handlers: activation +
/// seeding, upsert, line over-allocation guard, lot attachment, soft-delete
/// cascade-in-code, and ship (status + actual date).
/// </summary>
public class SalesOrderStagedScheduleHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private static IClock FixedClock()
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    private static async Task<SalesOrder> SeedOrderAsync(AppDbContext db, params decimal[] lineQuantities)
    {
        var customer = new Customer { Name = "Acme Corp" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var order = new SalesOrder
        {
            OrderNumber = "SO-0001",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
        };
        var lineNo = 1;
        foreach (var qty in lineQuantities)
        {
            order.Lines.Add(new SalesOrderLine
            {
                Description = $"Widget {lineNo}",
                Quantity = qty,
                UnitPrice = 100m,
                LineNumber = lineNo++,
            });
        }
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static ScheduleMilestoneModel DerivedFor(int lineId)
        => new(
            SalesOrderLineId: lineId,
            PartNumber: null,
            PartDescription: "Widget",
            DeliveryDate: Now.AddDays(30),
            ShipBy: Now.AddDays(28),
            QcCompleteBy: Now.AddDays(27),
            ProductionCompleteBy: Now.AddDays(27),
            ProductionStartBy: Now.AddDays(20),
            MaterialsNeededBy: Now.AddDays(20),
            PoOrderBy: Now.AddDays(10),
            IsAtRisk: false);

    // ---- Activate + seeding -------------------------------------------------

    [Fact]
    public async Task Activate_NoStages_SeedsSingleProductionToShipStageCoveringAllLines()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 5m, 3m);

        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetSalesOrderScheduleQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(order.Lines.Select(l => DerivedFor(l.Id)).ToList());
        mediator.Setup(m => m.Send(It.IsAny<GetSalesOrderStagesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SalesOrderStagesResponseModel)null!);

        var handler = new ActivateStagedScheduleHandler(db, mediator.Object);
        await handler.Handle(new ActivateStagedScheduleCommand(order.Id), CancellationToken.None);

        var stages = await db.SalesOrderStages.Include(s => s.Lines).ToListAsync();
        stages.Should().ContainSingle();
        var stage = stages[0];
        stage.Sequence.Should().Be(1);
        stage.Name.Should().Be("Production → Ship");
        stage.Status.Should().Be(SalesOrderStageStatus.Planned);
        // Rolled up to the latest per-line dates.
        stage.PlannedProductionComplete.Should().Be(Now.AddDays(27));
        stage.PlannedShipDate.Should().Be(Now.AddDays(28));
        // One stage line per SO line at full ordered quantity.
        stage.Lines.Should().HaveCount(2);
        stage.Lines.Select(l => l.Quantity).Should().BeEquivalentTo(new[] { 5m, 3m });

        db.ActivityLogs.Local.Should().ContainSingle(a =>
            a.Action == "staged-schedule-activated" && a.EntityType == "SalesOrder" && a.EntityId == order.Id);
    }

    [Fact]
    public async Task Activate_WhenAlreadyActivated_IsRejected()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 4m);
        db.SalesOrderStages.Add(new SalesOrderStage { SalesOrderId = order.Id, Sequence = 1, Name = "Existing" });
        await db.SaveChangesAsync();

        var mediator = new Mock<IMediator>();
        var handler = new ActivateStagedScheduleHandler(db, mediator.Object);

        var act = () => handler.Handle(new ActivateStagedScheduleCommand(order.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already activated*");
    }

    // ---- Get view -----------------------------------------------------------

    [Fact]
    public async Task GetStages_ReturnsStagesWithLinksAndDerivedTimeline()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 6m);
        var line = order.Lines.First();

        var shipment = new Shipment { ShipmentNumber = "SH-9", SalesOrderId = order.Id };
        db.Shipments.Add(shipment);
        var schedule = new PaymentSchedule { SalesOrderId = order.Id };
        var milestone = new PaymentMilestone { Sequence = 1, Name = "Deposit", Percentage = 100m };
        schedule.Milestones.Add(milestone);
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var stage = new SalesOrderStage
        {
            SalesOrderId = order.Id,
            Sequence = 1,
            Name = "Stage 1",
            ShipmentId = shipment.Id,
            PaymentMilestoneId = milestone.Id,
        };
        stage.Lines.Add(new SalesOrderStageLine { SalesOrderLineId = line.Id, Quantity = 6m });
        db.SalesOrderStages.Add(stage);
        db.LotRecords.Add(new LotRecord { LotNumber = "LOT-1", PartId = 0, Quantity = 6m, SalesOrderStageId = stage.Id });
        await db.SaveChangesAsync();

        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetSalesOrderScheduleQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleMilestoneModel> { DerivedFor(line.Id) });

        var handler = new GetSalesOrderStagesHandler(db, mediator.Object);
        var result = await handler.Handle(new GetSalesOrderStagesQuery(order.Id), CancellationToken.None);

        result.IsActivated.Should().BeTrue();
        result.Stages.Should().ContainSingle();
        var s = result.Stages[0];
        s.ShipmentNumber.Should().Be("SH-9");
        s.PaymentMilestoneName.Should().Be("Deposit");
        s.Lines.Should().ContainSingle().Which.Quantity.Should().Be(6m);
        s.Lots.Should().ContainSingle().Which.LotNumber.Should().Be("LOT-1");
        result.DerivedTimeline.Should().ContainSingle().Which.SalesOrderLineId.Should().Be(line.Id);
    }

    // ---- Upsert -------------------------------------------------------------

    [Fact]
    public async Task Upsert_CreateThenUpdate_AppendsAndEditsStage()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 2m);
        var handler = new UpsertSalesOrderStageHandler(db);

        var created = await handler.Handle(new UpsertSalesOrderStageCommand(
            null, order.Id, "First", 1, null, null, "notes", null), CancellationToken.None);

        created.Name.Should().Be("First");
        created.Sequence.Should().Be(1);
        (await db.SalesOrderStages.CountAsync()).Should().Be(1);
        db.ActivityLogs.Local.Should().Contain(a => a.Action == "stage-created");

        var updated = await handler.Handle(new UpsertSalesOrderStageCommand(
            created.Id, null, "Renamed", 2, Now, Now.AddDays(1), "new notes", null), CancellationToken.None);

        updated.Name.Should().Be("Renamed");
        updated.Sequence.Should().Be(2);
        updated.PlannedShipDate.Should().Be(Now.AddDays(1));
        (await db.SalesOrderStages.CountAsync()).Should().Be(1); // updated in place
        db.ActivityLogs.Local.Should().Contain(a => a.Action == "stage-updated");
    }

    [Fact]
    public void Upsert_Validator_RequiresNameAndSequence()
    {
        var validator = new UpsertSalesOrderStageValidator();

        validator.Validate(new UpsertSalesOrderStageCommand(null, 1, "", 1, null, null, null, null))
            .IsValid.Should().BeFalse();
        validator.Validate(new UpsertSalesOrderStageCommand(null, 1, "Ok", 0, null, null, null, null))
            .IsValid.Should().BeFalse();
        validator.Validate(new UpsertSalesOrderStageCommand(null, 1, "Ok", 1, null, null, null, null))
            .IsValid.Should().BeTrue();
    }

    // ---- Assign lines (over-allocation guard) -------------------------------

    [Fact]
    public async Task AssignLines_SumEqualToOrderedQuantity_IsAccepted()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 10m);
        var line = order.Lines.First();
        var (stageA, stageB) = await SeedTwoStagesAsync(db, order.Id);
        db.SalesOrderStageLines.Add(new SalesOrderStageLine
        {
            SalesOrderStageId = stageA.Id, SalesOrderLineId = line.Id, Quantity = 6m,
        });
        await db.SaveChangesAsync();

        var handler = new AssignStageLinesHandler(db, FixedClock());
        // 6 (stage A) + 4 (stage B) == 10 ordered → OK
        var result = await handler.Handle(new AssignStageLinesCommand(stageB.Id,
            [new StageLineAllocationModel(line.Id, 4m)]), CancellationToken.None);

        result.Lines.Should().ContainSingle().Which.Quantity.Should().Be(4m);
        db.ActivityLogs.Local.Should().Contain(a => a.Action == "stage-lines-assigned");
    }

    [Fact]
    public async Task AssignLines_SumExceedingOrderedQuantity_IsRejected()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 10m);
        var line = order.Lines.First();
        var (stageA, stageB) = await SeedTwoStagesAsync(db, order.Id);
        db.SalesOrderStageLines.Add(new SalesOrderStageLine
        {
            SalesOrderStageId = stageA.Id, SalesOrderLineId = line.Id, Quantity = 6m,
        });
        await db.SaveChangesAsync();

        var handler = new AssignStageLinesHandler(db, FixedClock());
        // 6 (stage A) + 5 (stage B) == 11 > 10 ordered → rejected
        var act = () => handler.Handle(new AssignStageLinesCommand(stageB.Id,
            [new StageLineAllocationModel(line.Id, 5m)]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceed*");
    }

    [Fact]
    public async Task AssignLines_ReplacesPreviousAllocationForStage()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 10m);
        var line = order.Lines.First();
        var (stageA, _) = await SeedTwoStagesAsync(db, order.Id);

        var handler = new AssignStageLinesHandler(db, FixedClock());
        await handler.Handle(new AssignStageLinesCommand(stageA.Id,
            [new StageLineAllocationModel(line.Id, 3m)]), CancellationToken.None);
        // Replace 3 with 8 on the same stage — the old row is soft-deleted, not summed.
        var result = await handler.Handle(new AssignStageLinesCommand(stageA.Id,
            [new StageLineAllocationModel(line.Id, 8m)]), CancellationToken.None);

        result.Lines.Should().ContainSingle().Which.Quantity.Should().Be(8m);
        var live = await db.SalesOrderStageLines.Where(l => l.SalesOrderStageId == stageA.Id).ToListAsync();
        live.Should().ContainSingle();
    }

    // ---- Assign lots --------------------------------------------------------

    [Fact]
    public async Task AssignLots_SetsForeignKey_AndDetachesRemoved()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 5m);
        var (stage, _) = await SeedTwoStagesAsync(db, order.Id);

        var part = new Part { PartNumber = "P1", Description = "Part", Status = PartStatus.Active };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        var lot1 = new LotRecord { LotNumber = "L1", PartId = part.Id, Quantity = 2m };
        var lot2 = new LotRecord { LotNumber = "L2", PartId = part.Id, Quantity = 3m, SalesOrderStageId = stage.Id };
        db.LotRecords.AddRange(lot1, lot2);
        await db.SaveChangesAsync();

        var handler = new AssignLotsToStageHandler(db);
        // Request only lot1 → lot1 attaches, lot2 (previously on the stage) detaches.
        var result = await handler.Handle(new AssignLotsToStageCommand(stage.Id, [lot1.Id]), CancellationToken.None);

        (await db.LotRecords.FirstAsync(l => l.Id == lot1.Id)).SalesOrderStageId.Should().Be(stage.Id);
        (await db.LotRecords.FirstAsync(l => l.Id == lot2.Id)).SalesOrderStageId.Should().BeNull();
        result.Lots.Should().ContainSingle().Which.LotNumber.Should().Be("L1");
        db.ActivityLogs.Local.Should().Contain(a => a.Action == "stage-lots-assigned");
    }

    // ---- Delete (cascade in code) ------------------------------------------

    [Fact]
    public async Task Delete_SoftDeletesStageAndLines_AndDetachesLots()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 5m);
        var line = order.Lines.First();
        var (stage, _) = await SeedTwoStagesAsync(db, order.Id);
        db.SalesOrderStageLines.Add(new SalesOrderStageLine
        {
            SalesOrderStageId = stage.Id, SalesOrderLineId = line.Id, Quantity = 5m,
        });
        var lot = new LotRecord { LotNumber = "L1", PartId = 0, Quantity = 5m, SalesOrderStageId = stage.Id };
        db.LotRecords.Add(lot);
        await db.SaveChangesAsync();

        var handler = new DeleteSalesOrderStageHandler(db, FixedClock());
        await handler.Handle(new DeleteSalesOrderStageCommand(stage.Id), CancellationToken.None);

        // Stage no longer visible under the soft-delete filter.
        (await db.SalesOrderStages.AnyAsync(s => s.Id == stage.Id)).Should().BeFalse();
        (await db.SalesOrderStages.IgnoreQueryFilters().FirstAsync(s => s.Id == stage.Id))
            .DeletedAt.Should().NotBeNull();
        // Stage lines soft-deleted.
        (await db.SalesOrderStageLines.AnyAsync(l => l.SalesOrderStageId == stage.Id)).Should().BeFalse();
        // Lot detached (not deleted).
        (await db.LotRecords.FirstAsync(l => l.Id == lot.Id)).SalesOrderStageId.Should().BeNull();
        db.ActivityLogs.Local.Should().Contain(a => a.Action == "stage-deleted");
    }

    // ---- Complete + Ship ----------------------------------------------------

    [Fact]
    public async Task Complete_MovesStageToReadyToShip()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 5m);
        var stage = new SalesOrderStage
        {
            SalesOrderId = order.Id, Sequence = 1, Name = "S", Status = SalesOrderStageStatus.Planned,
        };
        db.SalesOrderStages.Add(stage);
        await db.SaveChangesAsync();

        var handler = new CompleteStageHandler(db);
        var result = await handler.Handle(new CompleteStageCommand(stage.Id), CancellationToken.None);

        result.Status.Should().Be(nameof(SalesOrderStageStatus.ReadyToShip));
        db.ActivityLogs.Local.Should().Contain(a => a.Action == "stage-completed");
    }

    [Fact]
    public async Task Ship_SetsActualShipDateAndShippedStatus_WithoutTouchingMilestone()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 5m);

        var schedule = new PaymentSchedule { SalesOrderId = order.Id };
        var milestone = new PaymentMilestone
        {
            Sequence = 1, Name = "Balance", Percentage = 100m,
            DueTrigger = PaymentDueTrigger.OnShipment, Status = PaymentMilestoneStatus.Pending,
        };
        schedule.Milestones.Add(milestone);
        db.PaymentSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var stage = new SalesOrderStage
        {
            SalesOrderId = order.Id, Sequence = 1, Name = "S",
            Status = SalesOrderStageStatus.ReadyToShip, PaymentMilestoneId = milestone.Id,
        };
        db.SalesOrderStages.Add(stage);
        await db.SaveChangesAsync();

        var handler = new ShipStageHandler(db, FixedClock());
        var result = await handler.Handle(new ShipStageCommand(stage.Id, null), CancellationToken.None);

        result.Status.Should().Be(nameof(SalesOrderStageStatus.Shipped));
        result.ActualShipDate.Should().Be(Now);

        // The milestone's stored status is untouched — S2's evaluator derives Due on read.
        (await db.PaymentMilestones.FirstAsync(m => m.Id == milestone.Id))
            .Status.Should().Be(PaymentMilestoneStatus.Pending);
        db.ActivityLogs.Local.Should().Contain(a => a.Action == "stage-shipped");
    }

    [Fact]
    public async Task Ship_BeforeReadyToShip_IsRejected()
    {
        using var db = TestDbContextFactory.Create();
        var order = await SeedOrderAsync(db, 5m);
        var stage = new SalesOrderStage
        {
            SalesOrderId = order.Id, Sequence = 1, Name = "S", Status = SalesOrderStageStatus.Planned,
        };
        db.SalesOrderStages.Add(stage);
        await db.SaveChangesAsync();

        var handler = new ShipStageHandler(db, FixedClock());
        var act = () => handler.Handle(new ShipStageCommand(stage.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ready to ship*");
    }

    private static async Task<(SalesOrderStage A, SalesOrderStage B)> SeedTwoStagesAsync(AppDbContext db, int orderId)
    {
        var a = new SalesOrderStage { SalesOrderId = orderId, Sequence = 1, Name = "A" };
        var b = new SalesOrderStage { SalesOrderId = orderId, Sequence = 2, Name = "B" };
        db.SalesOrderStages.AddRange(a, b);
        await db.SaveChangesAsync();
        return (a, b);
    }
}
