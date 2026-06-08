using FluentAssertions;
using Forge.Api.Features.Jobs.ProductionRuns;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Jobs;

/// <summary>
/// F-049 regression: UpdateProductionRun must reject completedQty > targetQty (INV-SF2).
/// Boundary: completedQty == targetQty is allowed.
/// </summary>
public class UpdateProductionRunHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly UpdateProductionRunHandler _handler;

    public UpdateProductionRunHandlerTests()
    {
        _db = TestDbContextFactory.Create();
        _handler = new UpdateProductionRunHandler(_db);
    }

    private async Task<ProductionRun> SeedRunAsync(int targetQty)
    {
        var part = new Part { PartNumber = "P-PR-001", Name = "Test Part" };
        _db.Parts.Add(part);

        var job = new Job { JobNumber = "JOB-PR-001", Description = "Test Job" };
        _db.Jobs.Add(job);

        await _db.SaveChangesAsync();

        var run = new ProductionRun
        {
            JobId = job.Id,
            PartId = part.Id,
            RunNumber = "RUN-001",
            TargetQuantity = targetQty,
            Status = ProductionRunStatus.InProgress,
        };
        _db.ProductionRuns.Add(run);
        await _db.SaveChangesAsync();

        return run;
    }

    [Fact]
    public async Task Handle_CompletedPlusScrapExceedsTarget_ThrowsInvalidOperation_F049()
    {
        // 8 good + 3 scrap = 11 > targetQty 10 — must reject
        var run = await SeedRunAsync(targetQty: 10);

        var act = () => _handler.Handle(new UpdateProductionRunCommand(
            JobId: run.JobId,
            RunId: run.Id,
            CompletedQuantity: 8,
            ScrapQuantity: 3,
            Status: ProductionRunStatus.InProgress.ToString(),
            Notes: null,
            SetupTimeMinutes: null,
            RunTimeMinutes: null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot exceed target quantity*");
    }

    [Fact]
    public async Task Handle_CompletedPlusScrapEqualsTarget_Succeeds_F049()
    {
        // 7 good + 3 scrap = 10 == targetQty 10 — boundary must be accepted
        var run = await SeedRunAsync(targetQty: 10);

        var result = await _handler.Handle(new UpdateProductionRunCommand(
            JobId: run.JobId,
            RunId: run.Id,
            CompletedQuantity: 7,
            ScrapQuantity: 3,
            Status: ProductionRunStatus.Completed.ToString(),
            Notes: null,
            SetupTimeMinutes: null,
            RunTimeMinutes: null), CancellationToken.None);

        result.CompletedQuantity.Should().Be(7);
        result.ScrapQuantity.Should().Be(3);
        result.Status.Should().Be(ProductionRunStatus.Completed.ToString());
        // Yield = good / (good + scrap) = 7 / 10 = 70% (NOT the old (7−3)/7 = 57%).
        result.YieldPercentage.Should().Be(70m);
    }

    [Theory]
    [InlineData(7, 3, 70)]   // 7 good of 10 processed
    [InlineData(10, 0, 100)] // all good
    [InlineData(0, 5, 0)]    // all scrap
    [InlineData(0, 0, 0)]    // nothing processed → 0 (no divide-by-zero)
    public void YieldPercent_IsGoodOverGoodPlusScrap(int completed, int scrap, int expected)
    {
        ProductionRun.YieldPercent(completed, scrap).Should().Be(expected);
    }

    public void Dispose() => _db.Dispose();
}
