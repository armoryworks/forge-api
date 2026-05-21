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
    public async Task Handle_CompletedQuantityExceedsTarget_ThrowsInvalidOperation_F049()
    {
        var run = await SeedRunAsync(targetQty: 10);

        var act = () => _handler.Handle(new UpdateProductionRunCommand(
            JobId: run.JobId,
            RunId: run.Id,
            CompletedQuantity: 11,
            ScrapQuantity: 0,
            Status: ProductionRunStatus.InProgress.ToString(),
            Notes: null,
            SetupTimeMinutes: null,
            RunTimeMinutes: null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot exceed target quantity*");
    }

    [Fact]
    public async Task Handle_CompletedQuantityEqualsTarget_Succeeds_F049()
    {
        var run = await SeedRunAsync(targetQty: 10);

        var result = await _handler.Handle(new UpdateProductionRunCommand(
            JobId: run.JobId,
            RunId: run.Id,
            CompletedQuantity: 10,
            ScrapQuantity: 0,
            Status: ProductionRunStatus.Completed.ToString(),
            Notes: null,
            SetupTimeMinutes: null,
            RunTimeMinutes: null), CancellationToken.None);

        result.CompletedQuantity.Should().Be(10);
        result.Status.Should().Be(ProductionRunStatus.Completed.ToString());
    }

    public void Dispose() => _db.Dispose();
}
