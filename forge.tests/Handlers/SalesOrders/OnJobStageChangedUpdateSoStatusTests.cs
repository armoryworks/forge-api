using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.DomainEvents.Handlers;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

/// <summary>
/// Verifies the SO connective-tissue rule: production progress sets InProduction, but the job kanban never
/// sets Shipped — fulfillment status is owned by actual shipments. Guards against the prior dual-writer bug.
/// </summary>
public class OnJobStageChangedUpdateSoStatusTests
{
    private const int ProductionStageId = 1;
    private const int CompletionStageId = 2;
    private const int JobId = 100;

    private static async Task<AppDbContext> SeedAsync(SalesOrderStatus soStatus)
    {
        var db = TestDbContextFactory.Create();
        db.SalesOrders.Add(new SalesOrder { Id = 1, OrderNumber = "SO-1", CustomerId = 1, Status = soStatus });
        db.SalesOrderLines.Add(new SalesOrderLine { Id = 10, SalesOrderId = 1, Quantity = 5, UnitPrice = 1m, LineNumber = 1 });
        db.Jobs.Add(new Job { Id = JobId, JobNumber = "J-1", SalesOrderLineId = 10 });
        db.JobStages.Add(new JobStage { Id = ProductionStageId, Name = "In Production" });
        db.JobStages.Add(new JobStage { Id = CompletionStageId, Name = "Shipped" });
        await db.SaveChangesAsync();
        return db;
    }

    private static OnJobStageChanged_UpdateSoStatus Handler(AppDbContext db)
        => new(db, NullLogger<OnJobStageChanged_UpdateSoStatus>.Instance);

    [Fact]
    public async Task Job_entering_production_moves_confirmed_SO_to_InProduction()
    {
        await using var db = await SeedAsync(SalesOrderStatus.Confirmed);

        await Handler(db).Handle(new JobStageChangedEvent(JobId, 0, ProductionStageId, 1), CancellationToken.None);

        (await db.SalesOrders.FindAsync(1))!.Status.Should().Be(SalesOrderStatus.InProduction);
    }

    [Fact]
    public async Task Job_reaching_a_completion_stage_does_NOT_mark_the_SO_Shipped()
    {
        await using var db = await SeedAsync(SalesOrderStatus.InProduction);

        await Handler(db).Handle(new JobStageChangedEvent(JobId, 0, CompletionStageId, 1), CancellationToken.None);

        (await db.SalesOrders.FindAsync(1))!.Status.Should().Be(SalesOrderStatus.InProduction,
            "a job reaching a 'Shipped' kanban stage must not advance the SO — only a real shipment does");
    }
}
