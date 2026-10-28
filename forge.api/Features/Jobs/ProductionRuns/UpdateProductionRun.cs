using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Jobs.ProductionRuns;

public record UpdateProductionRunCommand(
    int JobId,
    int RunId,
    int CompletedQuantity,
    int ScrapQuantity,
    string Status,
    string? Notes,
    decimal? SetupTimeMinutes,
    decimal? RunTimeMinutes,
    int? ToolingAssetId = null) : IRequest<ProductionRunResponseModel>;

public class UpdateProductionRunCommandValidator : AbstractValidator<UpdateProductionRunCommand>
{
    public UpdateProductionRunCommandValidator()
    {
        RuleFor(x => x.CompletedQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Completed quantity cannot be negative.");

        RuleFor(x => x.ScrapQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Scrap quantity cannot be negative.");

        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required.")
            .Must(s => Enum.TryParse<ProductionRunStatus>(s, true, out _))
            .WithMessage("Invalid status value.");

        RuleFor(x => x.SetupTimeMinutes)
            .GreaterThanOrEqualTo(0).When(x => x.SetupTimeMinutes.HasValue)
            .WithMessage("Setup time cannot be negative.");

        RuleFor(x => x.RunTimeMinutes)
            .GreaterThanOrEqualTo(0).When(x => x.RunTimeMinutes.HasValue)
            .WithMessage("Run time cannot be negative.");
    }
}

public class UpdateProductionRunHandler(AppDbContext db, IClock clock) : IRequestHandler<UpdateProductionRunCommand, ProductionRunResponseModel>
{
    public async Task<ProductionRunResponseModel> Handle(UpdateProductionRunCommand request, CancellationToken cancellationToken)
    {
        var run = await db.ProductionRuns
            .Include(pr => pr.Job)
            .Include(pr => pr.Part)
            .FirstOrDefaultAsync(pr => pr.Id == request.RunId && pr.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Production run {request.RunId} not found on job {request.JobId}.");

        var newStatus = Enum.Parse<ProductionRunStatus>(request.Status, true);

        if (run.ReceivedToStockAt is not null
            && (request.CompletedQuantity != run.CompletedQuantity || request.ScrapQuantity != run.ScrapQuantity))
            throw new InvalidOperationException(
                $"Production run {run.RunNumber} has been received to stock, so its good and scrap quantities can no longer change.");

        var before = (run.CompletedQuantity, run.ScrapQuantity, run.Status);

        if (request.ToolingAssetId is int toolingAssetId)
            await ProductionRunTooling.ReassignAsync(db, run, toolingAssetId, cancellationToken);

        run.CompletedQuantity = request.CompletedQuantity;
        run.ScrapQuantity = request.ScrapQuantity;
        run.Notes = request.Notes;
        run.SetupTimeMinutes = request.SetupTimeMinutes;

        if (request.RunTimeMinutes is > 0)
        {
            run.RunTimeMinutes = request.RunTimeMinutes;
            run.RunTimeManual = true;
        }
        else if (request.RunTimeMinutes is not null || !run.RunTimeManual)
        {
            run.RunTimeMinutes = null;
            run.RunTimeManual = false;
        }

        if (newStatus == ProductionRunStatus.InProgress && run.StartedAt is null)
            run.StartedAt = clock.UtcNow;

        if (newStatus == ProductionRunStatus.Completed && run.CompletedAt is null)
            run.CompletedAt = clock.UtcNow;

        if (newStatus == ProductionRunStatus.Completed && !run.RunTimeManual)
            run.RunTimeMinutes = await ProductionRunTiming.DeriveRunTimeMinutesAsync(db, run, cancellationToken);

        run.Status = newStatus;

        var shotsAdded = await ProductionRunTooling.ApplyShotsAsync(db, run, cancellationToken);

        if (before != (run.CompletedQuantity, run.ScrapQuantity, run.Status) || shotsAdded != 0)
            db.LogActivityAt("production-run-updated",
                $"Production run {run.RunNumber}: {run.Status}, {run.CompletedQuantity} good, {run.ScrapQuantity} scrap"
                    + (run.CompletedQuantity > run.TargetQuantity
                        ? $", {run.CompletedQuantity - run.TargetQuantity} good over the target of {run.TargetQuantity}"
                        : string.Empty)
                    + (shotsAdded != 0 ? $", {shotsAdded:+#;-#} shots on tooling" : string.Empty),
                ("Job", run.JobId));

        await db.SaveChangesAsync(cancellationToken);

        return await ProductionRunResponses.BuildAsync(db, run, cancellationToken);
    }
}
