using FluentAssertions;
using Moq;

using Forge.Api.Features.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class JobEstimateAndPriceTests
{
    private static IStandardCostResolver Standard(decimal material, decimal labor, decimal overhead)
    {
        var resolver = new Mock<IStandardCostResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandardCostElements(material, labor, overhead));
        return resolver.Object;
    }

    private static async Task<Part> SeedPartAsync(AppDbContext db, decimal subcontractCost = 0m)
    {
        var part = new Part { PartNumber = $"P-EST-{Guid.NewGuid():N}", Name = "Estimate part" };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        if (subcontractCost > 0m)
        {
            db.Operations.Add(new Operation
            {
                PartId = part.Id, StepNumber = 1, Title = "Plate", IsSubcontract = true, SubcontractCost = subcontractCost,
            });
            await db.SaveChangesAsync();
        }
        return part;
    }

    [Fact]
    public async Task Snapshot_sets_estimates_from_the_standard_times_build_quantity()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db, subcontractCost: 1.5m);
        var job = new Job { JobNumber = "J-EST-1", Title = "Estimate", PartId = part.Id };

        await JobEstimates.SnapshotAsync(db, Standard(2m, 3m, 0.5m), job, 10m, default);

        job.EstimatedMaterialCost.Should().Be(20m);
        job.EstimatedLaborCost.Should().Be(30m);
        job.EstimatedBurdenCost.Should().Be(5m);
        job.EstimatedSubcontractCost.Should().Be(15m);
    }

    [Fact]
    public async Task Cost_summary_falls_back_to_the_standard_when_a_job_has_no_stored_estimate()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var job = new Job { JobNumber = "J-EST-2", Title = "Legacy", PartId = part.Id };
        job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 4m });
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var summary = await new JobCostService(db, null, Standard(5m, 2m, 1m)).GetCostSummaryAsync(job.Id, default);

        summary.MaterialEstimated.Should().Be(20m);
        summary.LaborEstimated.Should().Be(8m);
        summary.BurdenEstimated.Should().Be(4m);
        summary.TotalEstimated.Should().Be(32m);
    }

    [Fact]
    public async Task Cost_summary_prices_a_sales_order_job_from_its_line()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var customer = new Customer { Name = "Price customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var line = new SalesOrderLine { PartId = part.Id, Description = "Line", Quantity = 10m, UnitPrice = 12.5m, LineNumber = 1 };
        db.SalesOrders.Add(new SalesOrder
        {
            OrderNumber = "SO-PRICE-1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed, Lines = [line],
        });
        await db.SaveChangesAsync();
        var job = new Job { JobNumber = "J-PRICE-1", Title = "SO job", PartId = part.Id, SalesOrderLineId = line.Id };
        job.JobParts.Add(new JobPart { PartId = part.Id, Quantity = 4m });
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var summary = await new JobCostService(db).GetCostSummaryAsync(job.Id, default);

        summary.QuotedPrice.Should().Be(50m);
        summary.ActualMarginPercent.Should().Be(100m);
    }

    [Fact]
    public async Task Percentages_with_a_zero_base_are_null()
    {
        using var db = TestDbContextFactory.Create();
        var job = new Job { JobNumber = "J-PCT-1", Title = "No base" };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var summary = await new JobCostService(db).GetCostSummaryAsync(job.Id, default);

        summary.MaterialVariancePercent.Should().BeNull();
        summary.LaborVariancePercent.Should().BeNull();
        summary.TotalVariancePercent.Should().BeNull();
        summary.ActualMarginPercent.Should().BeNull();
        job.EstimatedMarginPercent.Should().BeNull();
    }
}
