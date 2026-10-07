using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services;

public class MrpOpenJobSupplyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static MrpService Service(AppDbContext db)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return new MrpService(db, clock.Object, new PartSourcingResolver(db), NullLogger<MrpService>.Instance);
    }

    private static async Task<Part> SeedPartAsync(AppDbContext db, ProcurementSource source = ProcurementSource.Make, string number = "MRP-JOB-01")
    {
        var part = new Part
        {
            PartNumber = number,
            Description = "Bracket",
            Status = PartStatus.Active,
            ProcurementSource = source,
            IsMrpPlanned = true,
            LotSizingRule = LotSizingRule.LotForLot,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part;
    }

    private static async Task<Part> SeedComponentAsync(AppDbContext db, int parentPartId, decimal perUnit, string number = "MRP-COMP-01")
    {
        var component = new Part
        {
            PartNumber = number,
            Description = "Component",
            Status = PartStatus.Active,
            LotSizingRule = LotSizingRule.LotForLot,
        };
        db.Parts.Add(component);
        await db.SaveChangesAsync();
        db.BOMLines.Add(new BOMLine { ParentPartId = parentPartId, ChildPartId = component.Id, Quantity = perUnit, SortOrder = 1 });
        await db.SaveChangesAsync();
        return component;
    }

    private static Task<List<MrpPlannedOrder>> PlannedOrdersForPartAsync(AppDbContext db, int runId, int partId)
        => db.MrpPlannedOrders.Where(o => o.MrpRunId == runId && o.PartId == partId).ToListAsync();

    private static async Task<SalesOrderLine> SeedSoLineAsync(AppDbContext db, int partId, decimal quantity, int daysOut)
    {
        var customer = new Customer { Name = $"Customer {daysOut}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = $"SO-{daysOut:D3}",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = Now.AddDays(daysOut),
        };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        var line = new SalesOrderLine
        {
            SalesOrderId = so.Id,
            PartId = partId,
            Description = "Bracket",
            Quantity = quantity,
            UnitPrice = 10m,
        };
        db.SalesOrderLines.Add(line);
        await db.SaveChangesAsync();
        return line;
    }

    private static Job NewJob(int partId, string number, decimal? jobPartQuantity = null)
    {
        var job = new Job
        {
            JobNumber = number,
            Title = number,
            TrackTypeId = 1,
            CurrentStageId = 1,
            PartId = partId,
        };
        if (jobPartQuantity is decimal quantity)
            job.JobParts.Add(new JobPart { PartId = partId, Quantity = quantity });
        return job;
    }

    private static Task<List<MrpPlannedOrder>> PlannedOrdersAsync(AppDbContext db, int runId)
        => db.MrpPlannedOrders.Where(o => o.MrpRunId == runId).ToListAsync();

    private static Task<List<MrpSupply>> JobSuppliesAsync(AppDbContext db, int runId)
        => db.MrpSupplies.Where(s => s.MrpRunId == runId && s.Source == MrpSupplySource.Job).ToListAsync();

    private static Task<List<MrpException>> ExceptionsAsync(AppDbContext db, int runId)
        => db.MrpExceptions.Where(e => e.MrpRunId == runId).ToListAsync();

    [Fact]
    public async Task OpenSoLinkedJob_SatisfiesItsLine_SoNoPlannedOrderIsSuggested()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var line = await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var job = NewJob(part.Id, "J-1");
        job.SalesOrderLineId = line.Id;
        job.DueDate = Now.AddDays(45);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await PlannedOrdersAsync(db, run.Id)).Should().BeEmpty();
        var supply = (await JobSuppliesAsync(db, run.Id)).Should().ContainSingle().Subject;
        supply.SourceEntityId.Should().Be(job.Id);
        supply.Quantity.Should().Be(100);
        supply.AvailableDate.Should().Be(Now.AddDays(45));
        (await ExceptionsAsync(db, run.Id)).Should().ContainSingle(e => e.ExceptionType == MrpExceptionType.Expedite)
            .Which.Message.Should().Contain("J-1");
    }

    [Fact]
    public async Task SoLinkedJob_IsPeggedToItsOwnLine_EvenWhenAnEarlierDemandCouldUseIt()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        await SeedSoLineAsync(db, part.Id, 40, daysOut: 10);
        var linkedLine = await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var job = NewJob(part.Id, "J-2");
        job.SalesOrderLineId = linkedLine.Id;
        job.DueDate = Now.AddDays(5);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        var planned = (await PlannedOrdersAsync(db, run.Id)).Should().ContainSingle().Subject;
        planned.Quantity.Should().Be(40);
        planned.DueDate.Should().Be(Now.AddDays(10));
    }

    [Fact]
    public async Task SoLinkedJob_CountsOnlyWhatIsStillOwedOnTheLine()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var line = await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);
        line.ShippedQuantity = 30;

        var job = NewJob(part.Id, "J-3");
        job.SalesOrderLineId = line.Id;
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(70);
        (await PlannedOrdersAsync(db, run.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task JobWithAnActiveProductionRun_IsCountedOnlyThroughTheRun()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var job = NewJob(part.Id, "J-4", jobPartQuantity: 100);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.ProductionRuns.Add(new ProductionRun
        {
            JobId = job.Id,
            PartId = part.Id,
            RunNumber = "PR-4",
            TargetQuantity = 100,
            Status = ProductionRunStatus.InProgress,
        });
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().BeEmpty();
        (await db.MrpSupplies.CountAsync(s => s.MrpRunId == run.Id && s.Source == MrpSupplySource.ProductionRun))
            .Should().Be(1);
        (await PlannedOrdersAsync(db, run.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ArchivedCompletedAndDisposedJobs_AreNotSupply()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var archived = NewJob(part.Id, "J-5A", jobPartQuantity: 100);
        archived.IsArchived = true;
        var completed = NewJob(part.Id, "J-5C", jobPartQuantity: 100);
        completed.CompletedDate = Now.AddDays(-1);
        var disposed = NewJob(part.Id, "J-5D", jobPartQuantity: 100);
        disposed.Disposition = JobDisposition.Scrap;
        db.Jobs.AddRange(archived, completed, disposed);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().BeEmpty();
        (await PlannedOrdersAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(100);
    }

    [Fact]
    public async Task ReceivedOutputOfCompletedRuns_ReducesTheJobSupply()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var job = NewJob(part.Id, "J-6", jobPartQuantity: 100);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.ProductionRuns.Add(new ProductionRun
        {
            JobId = job.Id,
            PartId = part.Id,
            RunNumber = "PR-6",
            TargetQuantity = 60,
            CompletedQuantity = 60,
            ReceivedQuantity = 60,
            Status = ProductionRunStatus.Completed,
        });
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        var supply = (await JobSuppliesAsync(db, run.Id)).Should().ContainSingle().Subject;
        supply.Quantity.Should().Be(40);
        supply.AvailableDate.Should().Be(Now.AddDays(14));
        (await PlannedOrdersAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(60);
    }

    [Fact]
    public async Task JobReleasedFromAPlannedOrder_SuppliesThePlannedQuantity()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var earlierRun = new MrpRun { RunNumber = "MRP-EARLIER", Status = MrpRunStatus.Completed, PlanningHorizonDays = 90 };
        db.MrpRuns.Add(earlierRun);
        await db.SaveChangesAsync();
        var plannedOrder = new MrpPlannedOrder
        {
            MrpRunId = earlierRun.Id,
            PartId = part.Id,
            OrderType = MrpOrderType.Manufacture,
            Status = MrpPlannedOrderStatus.Released,
            Quantity = 100,
            StartDate = Now,
            DueDate = Now.AddDays(30),
        };
        db.MrpPlannedOrders.Add(plannedOrder);
        await db.SaveChangesAsync();

        var job = NewJob(part.Id, "J-7");
        job.MrpPlannedOrderId = plannedOrder.Id;
        job.DueDate = plannedOrder.DueDate;
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(100);
        (await PlannedOrdersAsync(db, run.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task TwoJobsOnOneLine_CoverTheLineOnlyOnce_SoALaterLineStillGetsAPlannedOrder()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var line = await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);
        await SeedSoLineAsync(db, part.Id, 50, daysOut: 40);

        var autoJob = NewJob(part.Id, "J-8A", jobPartQuantity: 100);
        autoJob.SalesOrderLineId = line.Id;
        autoJob.DueDate = Now.AddDays(20);
        var manualJob = NewJob(part.Id, "J-8B", jobPartQuantity: 100);
        manualJob.SalesOrderLineId = line.Id;
        manualJob.DueDate = Now.AddDays(25);
        db.Jobs.AddRange(autoJob, manualJob);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Sum(s => s.Quantity).Should().Be(100);
        var planned = (await PlannedOrdersAsync(db, run.Id)).Should().ContainSingle().Subject;
        planned.Quantity.Should().Be(50);
        planned.DueDate.Should().Be(Now.AddDays(40));
    }

    [Fact]
    public async Task JobsSplittingOneLine_EachCountTheirOwnQuantity()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var line = await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var first = NewJob(part.Id, "J-9A", jobPartQuantity: 60);
        first.SalesOrderLineId = line.Id;
        var second = NewJob(part.Id, "J-9B", jobPartQuantity: 40);
        second.SalesOrderLineId = line.Id;
        db.Jobs.AddRange(first, second);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Select(s => s.Quantity).Should().BeEquivalentTo([60m, 40m]);
        (await PlannedOrdersAsync(db, run.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task JobSplitIntoRuns_CountsTheQuantityNotYetInARun()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        await SeedSoLineAsync(db, part.Id, 1000, daysOut: 30);

        var job = NewJob(part.Id, "J-10", jobPartQuantity: 1000);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.ProductionRuns.Add(new ProductionRun
        {
            JobId = job.Id,
            PartId = part.Id,
            RunNumber = "PR-10",
            TargetQuantity = 250,
            Status = ProductionRunStatus.InProgress,
        });
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(750);
        (await PlannedOrdersAsync(db, run.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task OpenJobLongPastItsDueDate_IsFlaggedAsPastDue()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        await SeedSoLineAsync(db, part.Id, 50, daysOut: 30);

        var job = NewJob(part.Id, "J-11", jobPartQuantity: 50);
        job.DueDate = Now.AddDays(-10);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await ExceptionsAsync(db, run.Id)).Should().ContainSingle(e => e.ExceptionType == MrpExceptionType.PastDue)
            .Which.Message.Should().Contain("J-11");
    }

    [Fact]
    public async Task PastDueJobOnAFullyShippedLine_IsNotSupply_AndRaisesNoException()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var line = await SeedSoLineAsync(db, part.Id, 50, daysOut: -20);
        line.ShippedQuantity = 50;

        var job = NewJob(part.Id, "J-12", jobPartQuantity: 50);
        job.SalesOrderLineId = line.Id;
        job.DueDate = Now.AddDays(-20);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().BeEmpty();
        (await ExceptionsAsync(db, run.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task PastDueException_QuotesTheQuantityLeftAfterTheLineCap()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var line = await SeedSoLineAsync(db, part.Id, 50, daysOut: 30);
        line.ShippedQuantity = 30;

        var job = NewJob(part.Id, "J-13", jobPartQuantity: 50);
        job.SalesOrderLineId = line.Id;
        job.DueDate = Now.AddDays(-10);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(20);
        (await ExceptionsAsync(db, run.Id)).Should().ContainSingle(e => e.ExceptionType == MrpExceptionType.PastDue)
            .Which.Message.Should().Contain("its 20 units");
    }

    [Fact]
    public async Task SoTrackingJobForABoughtPart_IsNotSupply_SoThePurchaseIsStillPlanned()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db, ProcurementSource.Buy);
        var line = await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var trackingJob = NewJob(part.Id, "J-14", jobPartQuantity: 100);
        trackingJob.SalesOrderLineId = line.Id;
        db.Jobs.Add(trackingJob);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().BeEmpty();
        var planned = (await PlannedOrdersAsync(db, run.Id)).Should().ContainSingle().Subject;
        planned.OrderType.Should().Be(MrpOrderType.Purchase);
        planned.Quantity.Should().Be(100);
    }

    [Fact]
    public async Task JobForABoughtPartWithABom_IsSupply()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db, ProcurementSource.Buy);
        await SeedComponentAsync(db, part.Id, perUnit: 1);
        var line = await SeedSoLineAsync(db, part.Id, 100, daysOut: 30);

        var job = NewJob(part.Id, "J-15", jobPartQuantity: 100);
        job.SalesOrderLineId = line.Id;
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(100);
        (await PlannedOrdersForPartAsync(db, run.Id, part.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task OpenJob_PlansItsComponents_ButNotItsParent()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var component = await SeedComponentAsync(db, part.Id, perUnit: 2);
        await SeedSoLineAsync(db, part.Id, 25, daysOut: 60);

        var job = NewJob(part.Id, "J-16", jobPartQuantity: 25);
        job.StartDate = Now.AddDays(20);
        job.DueDate = Now.AddDays(50);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await PlannedOrdersForPartAsync(db, run.Id, part.Id)).Should().BeEmpty();
        var demand = await db.MrpDemands.SingleAsync(d => d.MrpRunId == run.Id && d.PartId == component.Id);
        demand.Source.Should().Be(MrpDemandSource.DependentDemand);
        demand.Quantity.Should().Be(50);
        demand.RequiredDate.Should().Be(Now.AddDays(20));
        var planned = (await PlannedOrdersForPartAsync(db, run.Id, component.Id)).Should().ContainSingle().Subject;
        planned.OrderType.Should().Be(MrpOrderType.Purchase);
        planned.Quantity.Should().Be(50);
    }

    [Fact]
    public async Task OpenJobComponentDemand_NetsMaterialAlreadyIssuedToWip()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var component = await SeedComponentAsync(db, part.Id, perUnit: 2);

        var job = NewJob(part.Id, "J-17", jobPartQuantity: 25);
        job.StartDate = Now.AddDays(5);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.ProductionRuns.Add(new ProductionRun
        {
            JobId = job.Id,
            PartId = part.Id,
            RunNumber = "PR-17",
            TargetQuantity = 10,
            CompletedQuantity = 10,
            ReceivedQuantity = 10,
            Status = ProductionRunStatus.Completed,
        });
        db.MaterialIssues.AddRange(
            new MaterialIssue { JobId = job.Id, PartId = component.Id, Quantity = 34, IssueType = MaterialIssueType.Issue, IssuedAt = Now },
            new MaterialIssue { JobId = job.Id, PartId = component.Id, Quantity = 4, IssueType = MaterialIssueType.Return, IssuedAt = Now },
            new MaterialIssue { JobId = job.Id, PartId = component.Id, Quantity = 3, IssueType = MaterialIssueType.Scrap, IssuedAt = Now });
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await db.MrpDemands.SingleAsync(d => d.MrpRunId == run.Id && d.PartId == component.Id))
            .Quantity.Should().Be(20);
    }

    [Fact]
    public async Task OpenJobComponentDemand_UsesTheBomRevisionPinnedAtRelease()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        var currentComponent = await SeedComponentAsync(db, part.Id, perUnit: 2);
        var pinnedComponent = new Part { PartNumber = "MRP-COMP-OLD", Description = "Old component", Status = PartStatus.Active };
        db.Parts.Add(pinnedComponent);
        await db.SaveChangesAsync();

        var revision = new BomRevision { PartId = part.Id, RevisionNumber = 1, EffectiveDate = Now.AddDays(-30) };
        revision.Entries.Add(new BomRevisionLine { PartId = pinnedComponent.Id, Quantity = 3 });
        db.BomRevisions.Add(revision);
        await db.SaveChangesAsync();

        var job = NewJob(part.Id, "J-18", jobPartQuantity: 10);
        job.BomRevisionIdAtRelease = revision.Id;
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await db.MrpDemands.AnyAsync(d => d.MrpRunId == run.Id && d.PartId == currentComponent.Id)).Should().BeFalse();
        (await PlannedOrdersForPartAsync(db, run.Id, pinnedComponent.Id)).Should().ContainSingle()
            .Which.Quantity.Should().Be(30);
    }

    [Fact]
    public async Task OutputBinnedAgainstTheJob_IsNotCountedTwice()
    {
        using var db = TestDbContextFactory.Create();
        var part = await SeedPartAsync(db);
        await SeedSoLineAsync(db, part.Id, 150, daysOut: 30);

        var job = NewJob(part.Id, "J-19", jobPartQuantity: 100);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.BinContents.Add(new BinContent
        {
            LocationId = 1,
            EntityType = "part",
            EntityId = part.Id,
            Quantity = 60,
            JobId = job.Id,
            Status = BinContentStatus.Stored,
            PlacedAt = Now,
        });
        await db.SaveChangesAsync();

        var run = await Service(db).ExecuteRunAsync(new MrpRunOptions());

        (await JobSuppliesAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(40);
        (await PlannedOrdersAsync(db, run.Id)).Should().ContainSingle().Which.Quantity.Should().Be(50);
    }
}
