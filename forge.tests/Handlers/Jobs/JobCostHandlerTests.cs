using FluentAssertions;

using Forge.Api.Features.Admin;
using Forge.Api.Features.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

public class JobCostHandlerTests
{
    [Fact]
    public async Task GetJobCostSummary_ReturnsEstimatedCosts()
    {
        using var db = TestDbContextFactory.Create();
        var job = new Job
        {
            JobNumber = "J-COST-01",
            Title = "Cost Test",
            EstimatedMaterialCost = 500m,
            EstimatedLaborCost = 300m,
            EstimatedBurdenCost = 100m,
            EstimatedSubcontractCost = 50m,
            QuotedPrice = 1200m,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var service = new JobCostService(db);
        var handler = new GetJobCostSummaryHandler(service);

        var result = await handler.Handle(new GetJobCostSummaryQuery(job.Id), CancellationToken.None);

        result.JobId.Should().Be(job.Id);
        result.MaterialEstimated.Should().Be(500m);
        result.LaborEstimated.Should().Be(300m);
        result.BurdenEstimated.Should().Be(100m);
        result.SubcontractEstimated.Should().Be(50m);
        result.QuotedPrice.Should().Be(1200m);
        result.TotalEstimated.Should().Be(950m);
    }

    [Fact]
    public async Task GetJobCostSummary_IncludesActualMaterialCosts()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-001", Description = "Test", Status = PartStatus.Active };
        db.Parts.Add(part);
        var job = new Job
        {
            JobNumber = "J-COST-02",
            Title = "Material Cost Test",
            QuotedPrice = 1000m,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        db.MaterialIssues.AddRange(
            new MaterialIssue
            {
                JobId = job.Id,
                PartId = part.Id,
                Quantity = 10,
                UnitCost = 5m,
                IssuedById = 1,
                IssuedAt = DateTimeOffset.UtcNow,
                IssueType = MaterialIssueType.Issue,
            },
            new MaterialIssue
            {
                JobId = job.Id,
                PartId = part.Id,
                Quantity = 2,
                UnitCost = 5m,
                IssuedById = 1,
                IssuedAt = DateTimeOffset.UtcNow,
                IssueType = MaterialIssueType.Return,
            }
        );
        await db.SaveChangesAsync();

        var service = new JobCostService(db);
        var handler = new GetJobCostSummaryHandler(service);

        var result = await handler.Handle(new GetJobCostSummaryQuery(job.Id), CancellationToken.None);

        // 10*5 - 2*5 = 40
        result.MaterialActual.Should().Be(40m);
    }

    [Fact]
    public async Task GetJobMaterialIssues_ReturnsList()
    {
        using var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-002", Description = "Test Part", Status = PartStatus.Active };
        db.Parts.Add(part);
        var job = new Job { JobNumber = "J-MAT-01", Title = "Material Test" };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        db.MaterialIssues.Add(new MaterialIssue
        {
            JobId = job.Id,
            PartId = part.Id,
            Quantity = 5,
            UnitCost = 10m,
            IssuedById = 1,
            IssuedAt = DateTimeOffset.UtcNow,
            IssueType = MaterialIssueType.Issue,
        });
        await db.SaveChangesAsync();

        var handler = new GetJobMaterialIssuesHandler(db);
        var result = await handler.Handle(new GetJobMaterialIssuesQuery(job.Id), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].PartNumber.Should().Be("P-002");
        result[0].Quantity.Should().Be(5);
        result[0].TotalCost.Should().Be(50m);
    }

    [Fact]
    public async Task RecalculateJobCosts_UpdatesTimeEntryCosts()
    {
        using var db = TestDbContextFactory.Create();
        var job = new Job { JobNumber = "J-RECALC", Title = "Recalc Test" };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        // Add a labor rate
        db.LaborRates.Add(new LaborRate
        {
            UserId = 1,
            StandardRatePerHour = 50m,
            OvertimeRatePerHour = 75m,
            EffectiveFrom = new DateOnly(2020, 1, 1),
        });

        // Add a time entry with no cost set
        db.TimeEntries.Add(new TimeEntry
        {
            JobId = job.Id,
            UserId = 1,
            Date = new DateOnly(2026, 4, 12),
            DurationMinutes = 120,
        });
        await db.SaveChangesAsync();

        var service = new JobCostService(db);
        await service.RecalculateTimeEntryCostsAsync(job.Id, CancellationToken.None);

        var entry = db.TimeEntries.First(t => t.JobId == job.Id);
        // 120 min = 2 hours * $50/hr = $100
        entry.LaborCost.Should().Be(100m);
    }

    [Fact]
    public async Task CreateLaborRate_ClosePreviousRate()
    {
        using var db = TestDbContextFactory.Create();
        db.LaborRates.Add(new LaborRate
        {
            UserId = 1,
            StandardRatePerHour = 40m,
            OvertimeRatePerHour = 60m,
            EffectiveFrom = new DateOnly(2025, 1, 1),
        });
        await db.SaveChangesAsync();

        var handler = new CreateLaborRateHandler(db);
        var result = await handler.Handle(new CreateLaborRateCommand(
            1, 50m, 75m, null, new DateOnly(2026, 1, 1), "Raise"), CancellationToken.None);

        result.StandardRatePerHour.Should().Be(50m);

        var previous = db.LaborRates.First(r => r.StandardRatePerHour == 40m);
        previous.EffectiveTo.Should().Be(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public async Task GetLaborRates_ReturnsSorted()
    {
        using var db = TestDbContextFactory.Create();
        db.LaborRates.AddRange(
            new LaborRate { UserId = 1, StandardRatePerHour = 30m, OvertimeRatePerHour = 45m, EffectiveFrom = new DateOnly(2024, 1, 1), EffectiveTo = new DateOnly(2024, 12, 31) },
            new LaborRate { UserId = 1, StandardRatePerHour = 40m, OvertimeRatePerHour = 60m, EffectiveFrom = new DateOnly(2025, 1, 1) }
        );
        await db.SaveChangesAsync();

        var handler = new GetLaborRatesHandler(db);
        var result = await handler.Handle(new GetLaborRatesQuery(1), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].StandardRatePerHour.Should().Be(40m); // Most recent first
    }
}
