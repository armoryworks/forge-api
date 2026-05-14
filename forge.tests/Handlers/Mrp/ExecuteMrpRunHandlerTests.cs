using FluentAssertions;

using Forge.Api.Features.Mrp;
using Forge.Api.Services;
using Forge.Core.Entities;
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
