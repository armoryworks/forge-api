using System.Text.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Reports;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Costing;

public class TimeEntryPricingTests
{
    private static readonly DateOnly Day = new(2026, 10, 7);

    private static async Task<(AppDbContext Db, Job Job, WorkCenter Wc, Operation Op)> SeedAsync(
        decimal wcLabor = 40m, decimal wcBurden = 10m, decimal opLabor = 0m, decimal opBurden = 0m)
    {
        var db = TestDbContextFactory.Create();
        var part = new Part { PartNumber = "P-PRICE", Name = "Part" };
        var wc = new WorkCenter { Name = "Mill", Code = $"WC-{Guid.NewGuid():N}", LaborCostPerHour = wcLabor, BurdenRatePerHour = wcBurden, IsActive = true };
        db.AddRange(part, wc);
        await db.SaveChangesAsync();
        var op = new Operation { PartId = part.Id, StepNumber = 1, Title = "Mill", WorkCenterId = wc.Id, LaborRate = opLabor, BurdenRate = opBurden };
        var job = new Job { JobNumber = "J-PRICE", Title = "Price", PartId = part.Id };
        db.AddRange(op, job);
        await db.SaveChangesAsync();
        return (db, job, wc, op);
    }

    private static TimeEntry Entry(Job job, Operation op, int minutes, int userId = 9) => new()
    {
        JobId = job.Id,
        UserId = userId,
        OperationId = op.Id,
        Date = Day,
        DurationMinutes = minutes,
    };

    [Fact]
    public async Task Without_a_user_labor_rate_the_work_center_rates_price_the_entry()
    {
        var (db, job, _, op) = await SeedAsync();
        await using var _ = db;
        var entry = Entry(job, op, 90);

        await new JobCostService(db).PriceTimeEntryAsync(entry, default);

        entry.LaborCost.Should().Be(60m);
        entry.ActualLaborCost.Should().Be(60m);
        entry.BurdenCost.Should().Be(15m);
    }

    [Fact]
    public async Task The_user_labor_rate_wins_and_the_operation_rates_fill_gaps()
    {
        var (db, job, _, op) = await SeedAsync(wcLabor: 0m, wcBurden: 10m, opLabor: 30m, opBurden: 20m);
        await using var _ = db;
        var withoutRate = Entry(job, op, 60, userId: 1);
        var withRate = Entry(job, op, 60, userId: 2);
        db.LaborRates.Add(new LaborRate { UserId = 2, StandardRatePerHour = 50m, ActualRatePerHour = 55m, EffectiveFrom = Day.AddDays(-30) });
        await db.SaveChangesAsync();

        var service = new JobCostService(db);
        await service.PriceTimeEntryAsync(withoutRate, default);
        await service.PriceTimeEntryAsync(withRate, default);

        withoutRate.LaborCost.Should().Be(30m);
        withoutRate.BurdenCost.Should().Be(20m);
        withRate.LaborCost.Should().Be(50m);
        withRate.ActualLaborCost.Should().Be(55m);
    }

    [Fact]
    public async Task Departmental_costing_prices_burden_as_a_share_of_labor_like_the_standard()
    {
        var (db, job, wc, op) = await SeedAsync();
        await using var _ = db;
        db.Add(new CostingProfile
        {
            Code = "default",
            Mode = "departmental",
            DepartmentalRates = JsonSerializer.Serialize(new[] { new { work_center_id = wc.Id, rate_pct = 150m } }, CostingConversionPolicy.DeptRateJson),
            EffectiveFrom = Day.AddDays(-1),
        });
        await db.SaveChangesAsync();
        var entry = Entry(job, op, 60);

        await new JobCostService(db, StubCapabilitySnapshotProvider.Tier2On).PriceTimeEntryAsync(entry, default);

        entry.LaborCost.Should().Be(40m);
        entry.BurdenCost.Should().Be(60m);
    }

    [Fact]
    public async Task Pricing_unpriced_entries_leaves_priced_ones_alone()
    {
        var (db, job, _, op) = await SeedAsync();
        await using var _ = db;
        var unpriced = Entry(job, op, 60);
        var priced = Entry(job, op, 60);
        priced.LaborCost = 99m;
        db.TimeEntries.AddRange(unpriced, priced);
        await db.SaveChangesAsync();

        await new JobCostService(db).PriceUnpricedTimeEntriesAsync(job.Id, default);

        unpriced.LaborCost.Should().Be(40m);
        priced.LaborCost.Should().Be(99m);
    }

    [Fact]
    public async Task Stopping_a_timer_prices_its_entry()
    {
        var h = new TimerTestHarness();
        var user = await h.AddUserAsync();
        var wc = new WorkCenter { Name = "Lathe", Code = "WC-LATHE", LaborCostPerHour = 30m, BurdenRatePerHour = 12m, IsActive = true };
        h.Db.Add(wc);
        await h.Db.SaveChangesAsync();
        var op = new Operation { PartId = 1, StepNumber = 1, Title = "Turn", WorkCenterId = wc.Id };
        h.Db.Add(op);
        await h.Db.SaveChangesAsync();
        var job = await h.AddJobAsync("J-TIMER-PRICE");
        var entry = await h.AddRunningTimerAsync(user.Id, job.Id, h.Now.AddMinutes(-120), op.Id);

        var stopService = h.TimerStopService();
        stopService.Close(entry, h.Now);
        await h.Db.SaveChangesAsync();
        await stopService.PublishStoppedAsync([entry], default);

        var stored = await h.Db.TimeEntries.AsNoTracking().SingleAsync(t => t.Id == entry.Id);
        stored.LaborCost.Should().Be(60m);
        stored.BurdenCost.Should().Be(24m);
    }

    [Fact]
    public async Task Labor_uses_the_split_labor_minutes_and_a_labor_basis_burdens_them_too()
    {
        var (db, job, _, op) = await SeedAsync();
        await using var _ = db;
        var entry = Entry(job, op, 60);
        entry.LaborMinutes = 15m;

        await new JobCostService(db).PriceTimeEntryAsync(entry, default);

        entry.LaborCost.Should().Be(10m);
        entry.ActualLaborCost.Should().Be(10m);
        entry.BurdenCost.Should().Be(2.5m);
    }

    [Fact]
    public async Task A_machine_basis_work_center_burdens_the_full_elapsed_time()
    {
        var (db, job, wc, op) = await SeedAsync();
        await using var _ = db;
        wc.BurdenBasis = Forge.Core.Enums.BurdenBasis.Machine;
        await db.SaveChangesAsync();
        var entry = Entry(job, op, 60);
        entry.LaborMinutes = 15m;

        await new JobCostService(db).PriceTimeEntryAsync(entry, default);

        entry.LaborCost.Should().Be(10m);
        entry.BurdenCost.Should().Be(10m);
    }

    private static async Task<Job> AddOrderLinkedJobAsync(AppDbContext db)
    {
        var customer = new Customer { Name = "Acme" };
        db.Add(customer);
        await db.SaveChangesAsync();
        var order = new SalesOrder { OrderNumber = "SO-PRICE", CustomerId = customer.Id };
        db.Add(order);
        await db.SaveChangesAsync();
        var part = new Part { PartNumber = "P-SO", Name = "Part" };
        db.Add(part);
        await db.SaveChangesAsync();
        var line = new SalesOrderLine { SalesOrderId = order.Id, PartId = part.Id, Quantity = 10m, UnitPrice = 25m, LineNumber = 1, Description = "Part" };
        db.Add(line);
        await db.SaveChangesAsync();
        var job = new Job
        {
            JobNumber = "J-SO-PRICE", Title = "SO price", PartId = part.Id, CustomerId = customer.Id,
            SalesOrderLineId = line.Id, CompletedDate = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero),
        };
        db.Add(job);
        await db.SaveChangesAsync();
        db.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = 4m });
        await db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Cost_summary_and_profitability_price_an_order_linked_job_from_its_order_line()
    {
        await using var db = TestDbContextFactory.Create();
        var job = await AddOrderLinkedJobAsync(db);

        var summary = await new JobCostService(db).GetCostSummaryAsync(job.Id, default);
        var report = await new GetJobProfitabilityReportHandler(db)
            .Handle(new GetJobProfitabilityReportQuery(null, null, null, null), default);

        summary.QuotedPrice.Should().Be(100m);
        report.Should().ContainSingle(r => r.JobId == job.Id).Which.QuotedPrice.Should().Be(100m);
    }

    [Fact]
    public async Task Percentages_with_no_base_are_null_not_zero()
    {
        await using var db = TestDbContextFactory.Create();
        var job = new Job { JobNumber = "J-NOBASE", Title = "No base" };
        db.Add(job);
        await db.SaveChangesAsync();

        var summary = await new JobCostService(db).GetCostSummaryAsync(job.Id, default);

        summary.ActualMarginPercent.Should().BeNull();
        summary.TotalVariancePercent.Should().BeNull();
    }
}
