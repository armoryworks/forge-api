using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Jobs.ProductionRuns;

public record CreateProductionRunCommand(
    int JobId,
    int PartId,
    int TargetQuantity,
    int? OperatorId,
    string? Notes,
    int? WorkCenterId = null,
    int? ToolingAssetId = null) : IRequest<ProductionRunResponseModel>;

public class CreateProductionRunCommandValidator : AbstractValidator<CreateProductionRunCommand>
{
    public CreateProductionRunCommandValidator()
    {
        RuleFor(x => x.JobId)
            .GreaterThan(0).WithMessage("JobId is required.");

        RuleFor(x => x.PartId)
            .GreaterThan(0).WithMessage("PartId is required.");

        RuleFor(x => x.TargetQuantity)
            .GreaterThan(0).WithMessage("Target quantity must be greater than zero.");
    }
}

public class CreateProductionRunHandler(AppDbContext db, IClock clock) : IRequestHandler<CreateProductionRunCommand, ProductionRunResponseModel>
{
    public async Task<ProductionRunResponseModel> Handle(CreateProductionRunCommand request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.FindAsync([request.JobId], cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        var part = await db.Parts.FindAsync([request.PartId], cancellationToken)
            ?? throw new KeyNotFoundException($"Part with ID {request.PartId} not found.");

        var datePrefix = ClockStateRules.LocalToday(await ClockStateRules.ShopTimeZoneAsync(db, cancellationToken), clock.UtcNow).ToString("yyyyMMdd");
        var todayCount = await db.ProductionRuns
            .Where(pr => pr.RunNumber.StartsWith($"RUN-{datePrefix}-"))
            .CountAsync(cancellationToken);

        var runNumber = $"RUN-{datePrefix}-{(todayCount + 1):D3}";

        var workCenterId = await ResolveWorkCenterIdAsync(request, cancellationToken);

        if (request.ToolingAssetId is int requestedTool)
            await ProductionRunTooling.RequireToolingAsync(db, requestedTool, cancellationToken);
        var toolingAssetId = request.ToolingAssetId
            ?? await ProductionRunTooling.DefaultToolingAsync(db, part, workCenterId, cancellationToken);

        var productionRun = new ProductionRun
        {
            WorkCenterId = workCenterId,
            ToolingAssetId = toolingAssetId,
            JobId = request.JobId,
            PartId = request.PartId,
            OperatorId = request.OperatorId,
            RunNumber = runNumber,
            TargetQuantity = request.TargetQuantity,
            Notes = request.Notes,
            Job = job,
            Part = part,
        };

        db.ProductionRuns.Add(productionRun);
        db.LogActivityAt("production-run-created",
            $"Production run {runNumber} created for {part.PartNumber}, target {request.TargetQuantity}",
            ("Job", job.Id));
        await db.SaveChangesAsync(cancellationToken);

        return await ProductionRunResponses.BuildAsync(db, productionRun, cancellationToken);
    }

    private async Task<int?> ResolveWorkCenterIdAsync(CreateProductionRunCommand request, CancellationToken cancellationToken)
    {
        if (request.WorkCenterId is int requested)
        {
            var exists = await db.WorkCenters.AnyAsync(w => w.Id == requested, cancellationToken);
            return exists ? requested : throw new KeyNotFoundException($"Work center {requested} not found.");
        }

        var next = await KioskWork.NextOperationsAsync(db, [request.JobId], tracking: true, cancellationToken);
        if (next.TryGetValue(request.JobId, out var operation) && operation.WorkCenterId is int current)
            return current;

        return await db.Operations
            .AsNoTracking()
            .Where(o => o.PartId == request.PartId && o.WorkCenterId != null)
            .OrderBy(o => o.StepNumber)
            .ThenBy(o => o.Id)
            .Select(o => o.WorkCenterId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
