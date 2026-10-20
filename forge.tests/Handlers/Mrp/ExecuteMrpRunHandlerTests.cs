using FluentAssertions;

using Forge.Api.Features.Mrp;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

using Microsoft.Extensions.Logging.Abstractions;

namespace Forge.Tests.Handlers.Mrp;

public class ExecuteMrpRunHandlerTests
{
    [Fact]
    public async Task Handle_NoMrpPlannedParts_CompletesWithZeroCounts()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var clock = new SystemClock();
        var logger = NullLogger<MrpService>.Instance;
        var mrpService = new MrpService(db, clock, new PartSourcingResolver(db), logger);

        var handler = new ExecuteMrpRunHandler(mrpService);
        var command = new ExecuteMrpRunCommand(MrpRunType.Full, 90, null, null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(MrpRunStatus.Completed);
        result.PlannedOrderCount.Should().Be(0);
        result.ExceptionCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ActiveMpsPartNotFlaggedForMrp_IsCountedAsUnplannedDemand()
    {
        using var db = TestDbContextFactory.Create();
        var clock = new SystemClock();
        var unflagged = new Part { PartNumber = "FG-1", Description = "Finished good", Status = PartStatus.Active, IsMrpPlanned = false };
        var flagged = new Part { PartNumber = "FG-2", Description = "Planned good", Status = PartStatus.Active, IsMrpPlanned = true };
        var draftOnly = new Part { PartNumber = "FG-3", Description = "Draft only", Status = PartStatus.Active, IsMrpPlanned = false };
        db.Parts.AddRange(unflagged, flagged, draftOnly);
        await db.SaveChangesAsync();
        var active = new MasterSchedule { Name = "Active", Status = MasterScheduleStatus.Active, PeriodStart = clock.UtcNow, PeriodEnd = clock.UtcNow.AddDays(30) };
        active.Lines.Add(new MasterScheduleLine { PartId = unflagged.Id, Quantity = 10m, DueDate = clock.UtcNow.AddDays(7) });
        active.Lines.Add(new MasterScheduleLine { PartId = flagged.Id, Quantity = 10m, DueDate = clock.UtcNow.AddDays(7) });
        var draft = new MasterSchedule { Name = "Draft", Status = MasterScheduleStatus.Draft, PeriodStart = clock.UtcNow, PeriodEnd = clock.UtcNow.AddDays(30) };
        draft.Lines.Add(new MasterScheduleLine { PartId = draftOnly.Id, Quantity = 10m, DueDate = clock.UtcNow.AddDays(7) });
        db.MasterSchedules.AddRange(active, draft);
        await db.SaveChangesAsync();
        var handler = new ExecuteMrpRunHandler(new MrpService(db, clock, new PartSourcingResolver(db), NullLogger<MrpService>.Instance));

        var result = await handler.Handle(new ExecuteMrpRunCommand(MrpRunType.Full, 90, null, null), CancellationToken.None);

        result.UnplannedDemandPartCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithDemandNoSupply_CreatesPurchasePlannedOrder()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var clock = new SystemClock();
        var logger = NullLogger<MrpService>.Instance;

        var part = new Part
        {
            PartNumber = "MRP-PART-001",
            Description = "Test MRP Part",
            Status = PartStatus.Active,
            IsMrpPlanned = true,
            LotSizingRule = LotSizingRule.LotForLot,
        };
        db.Parts.Add(part);
        // Lead time lives on the preferred VendorPart row now; the MRP
        // service falls back to a 14-day default when no VendorPart is
        // configured, which is fine for this test.

        var customer = new Customer { Name = "Test Customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = "SO-001",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = clock.UtcNow.AddDays(30),
        };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var soLine = new SalesOrderLine
        {
            SalesOrderId = so.Id,
            PartId = part.Id,
            Description = "Test Line",
            Quantity = 100,
            UnitPrice = 10m,
        };
        db.SalesOrderLines.Add(soLine);
        await db.SaveChangesAsync();

        var mrpService = new MrpService(db, clock, new PartSourcingResolver(db), logger);
        var handler = new ExecuteMrpRunHandler(mrpService);
        var command = new ExecuteMrpRunCommand(MrpRunType.Full, 90, null, null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(MrpRunStatus.Completed);
        result.TotalDemandCount.Should().BeGreaterThan(0);
        result.PlannedOrderCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_ConcurrentRunBlocked_ThrowsInvalidOperation()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var clock = new SystemClock();
        var logger = NullLogger<MrpService>.Instance;

        // Create an existing running MRP run
        db.MrpRuns.Add(new MrpRun
        {
            RunNumber = "MRP-RUNNING",
            RunType = MrpRunType.Full,
            Status = MrpRunStatus.Running,
            PlanningHorizonDays = 90,
        });
        await db.SaveChangesAsync();

        var mrpService = new MrpService(db, clock, new PartSourcingResolver(db), logger);
        var handler = new ExecuteMrpRunHandler(mrpService);
        var command = new ExecuteMrpRunCommand(MrpRunType.Full, 90, null, null);

        // Act & Assert
        var act = () => handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already in progress*");
    }

    [Fact]
    public async Task Handle_Simulation_SetsIsSimulationFlag()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var clock = new SystemClock();
        var logger = NullLogger<MrpService>.Instance;
        var mrpService = new MrpService(db, clock, new PartSourcingResolver(db), logger);

        var handler = new SimulateMrpRunHandler(mrpService);
        var command = new SimulateMrpRunCommand(MrpRunType.Simulation, 60, null, null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSimulation.Should().BeTrue();
        result.Status.Should().Be(MrpRunStatus.Completed);
    }
}
