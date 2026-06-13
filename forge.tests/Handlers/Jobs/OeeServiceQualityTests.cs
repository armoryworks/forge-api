using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// OEE Quality factor uses the canonical production-run semantics: CompletedQuantity is the GOOD count
/// (disjoint from ScrapQuantity), so total units processed = good + scrap + rework and Quality = good / total.
/// (The old code treated CompletedQuantity as the total and double-subtracted scrap, understating Quality.)
/// </summary>
public class OeeServiceQualityTests : IDisposable
{
    private readonly AppDbContext _db;

    public OeeServiceQualityTests() => _db = TestDbContextFactory.Create();

    [Fact]
    public async Task Quality_IsGoodOverTotalProcessed()
    {
        var wc = new WorkCenter { Name = "WC-1", DailyCapacityHours = 8m, IdealCycleTimeSeconds = 60m, IsActive = true };
        _db.Add(wc);
        await _db.SaveChangesAsync();

        var part = new Part { PartNumber = "P-OEE-1", Name = "Part" };
        _db.Add(part);
        var job = new Job { JobNumber = "JOB-OEE-1", Description = "Job" };
        _db.Add(job);
        await _db.SaveChangesAsync();

        // 8 good + 2 scrap = 10 processed → Quality = 0.8.
        _db.Add(new ProductionRun
        {
            JobId = job.Id, PartId = part.Id, WorkCenterId = wc.Id, RunNumber = "RUN-OEE-1",
            TargetQuantity = 10, CompletedQuantity = 8, ScrapQuantity = 2, ReworkQuantity = 0,
            Status = ProductionRunStatus.Completed, RunTimeMinutes = 10m,
            StartedAt = new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero),
        });
        await _db.SaveChangesAsync();

        var result = await new OeeService(_db).CalculateOeeAsync(
            wc.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), default);

        result.GoodQuantity.Should().Be(8m);
        result.ScrapQuantity.Should().Be(2m);
        result.TotalQuantity.Should().Be(10m, "total processed = good + scrap + rework");
        result.Quality.Should().Be(0.8m, "8 good of 10 processed — NOT the old (8−2)/8 = 0.75");
    }

    public void Dispose() => _db.Dispose();
}
