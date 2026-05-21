using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

public record UpdateProductionRunCommand(
    int JobId,
    int RunId,
    int CompletedQuantity,
    int ScrapQuantity,
    string Status,
    string? Notes,
    decimal? SetupTimeMinutes,
    decimal? RunTimeMinutes) : IRequest<ProductionRunResponseModel>;

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

public class UpdateProductionRunHandler(AppDbContext db) : IRequestHandler<UpdateProductionRunCommand, ProductionRunResponseModel>
{
    public async Task<ProductionRunResponseModel> Handle(UpdateProductionRunCommand request, CancellationToken cancellationToken)
    {
        var run = await db.ProductionRuns
            .Include(pr => pr.Job)
            .Include(pr => pr.Part)
            .FirstOrDefaultAsync(pr => pr.Id == request.RunId && pr.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Production run {request.RunId} not found on job {request.JobId}.");

        var newStatus = Enum.Parse<ProductionRunStatus>(request.Status, true);

        if (request.CompletedQuantity + request.ScrapQuantity > run.TargetQuantity)
            throw new InvalidOperationException(
                $"Completed quantity ({request.CompletedQuantity}) + scrap ({request.ScrapQuantity}) cannot exceed target quantity ({run.TargetQuantity}).");

        run.CompletedQuantity = request.CompletedQuantity;
        run.ScrapQuantity = request.ScrapQuantity;
        run.Notes = request.Notes;
        run.SetupTimeMinutes = request.SetupTimeMinutes;
        run.RunTimeMinutes = request.RunTimeMinutes;

        if (newStatus == ProductionRunStatus.InProgress && run.StartedAt is null)
            run.StartedAt = DateTimeOffset.UtcNow;

        if (newStatus == ProductionRunStatus.Completed && run.CompletedAt is null)
            run.CompletedAt = DateTimeOffset.UtcNow;

        run.Status = newStatus;

        await db.SaveChangesAsync(cancellationToken);

        string? operatorName = null;
        if (run.OperatorId.HasValue)
        {
            var user = await db.Users.FindAsync([run.OperatorId.Value], cancellationToken);
            if (user is not null)
                operatorName = $"{user.FirstName} {user.LastName}".Trim();
        }

        var yieldPct = run.CompletedQuantity > 0
            ? (run.CompletedQuantity - run.ScrapQuantity) * 100.0m / run.CompletedQuantity
            : 0m;

        return new ProductionRunResponseModel(
            run.Id,
            run.JobId,
            run.Job.JobNumber,
            run.PartId,
            run.Part.PartNumber,
            run.Part.Description ?? run.Part.Name,
            run.OperatorId,
            operatorName,
            run.RunNumber,
            run.TargetQuantity,
            run.CompletedQuantity,
            run.ScrapQuantity,
            run.Status.ToString(),
            run.StartedAt,
            run.CompletedAt,
            run.Notes,
            run.SetupTimeMinutes,
            run.RunTimeMinutes,
            yieldPct);
    }
}
