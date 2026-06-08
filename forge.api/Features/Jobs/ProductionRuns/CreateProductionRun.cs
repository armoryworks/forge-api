using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

public record CreateProductionRunCommand(
    int JobId,
    int PartId,
    int TargetQuantity,
    int? OperatorId,
    string? Notes) : IRequest<ProductionRunResponseModel>;

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

public class CreateProductionRunHandler(AppDbContext db) : IRequestHandler<CreateProductionRunCommand, ProductionRunResponseModel>
{
    public async Task<ProductionRunResponseModel> Handle(CreateProductionRunCommand request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.FindAsync([request.JobId], cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        var part = await db.Parts.FindAsync([request.PartId], cancellationToken)
            ?? throw new KeyNotFoundException($"Part with ID {request.PartId} not found.");

        var datePrefix = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var todayCount = await db.ProductionRuns
            .Where(pr => pr.RunNumber.StartsWith($"RUN-{datePrefix}-"))
            .CountAsync(cancellationToken);

        var runNumber = $"RUN-{datePrefix}-{(todayCount + 1):D3}";

        string? operatorName = null;
        if (request.OperatorId.HasValue)
        {
            var user = await db.Users.FindAsync([request.OperatorId.Value], cancellationToken);
            if (user is not null)
                operatorName = $"{user.FirstName} {user.LastName}".Trim();
        }

        var productionRun = new ProductionRun
        {
            JobId = request.JobId,
            PartId = request.PartId,
            OperatorId = request.OperatorId,
            RunNumber = runNumber,
            TargetQuantity = request.TargetQuantity,
            Notes = request.Notes,
        };

        db.ProductionRuns.Add(productionRun);
        await db.SaveChangesAsync(cancellationToken);

        return new ProductionRunResponseModel(
            productionRun.Id,
            productionRun.JobId,
            job.JobNumber,
            productionRun.PartId,
            part.PartNumber,
            part.Description ?? part.Name,
            productionRun.OperatorId,
            operatorName,
            productionRun.RunNumber,
            productionRun.TargetQuantity,
            productionRun.CompletedQuantity,
            productionRun.ScrapQuantity,
            productionRun.Status.ToString(),
            productionRun.StartedAt,
            productionRun.CompletedAt,
            productionRun.Notes,
            productionRun.SetupTimeMinutes,
            productionRun.RunTimeMinutes,
            0m,
            productionRun.ReceivedQuantity,
            productionRun.ReceivedToStockAt);
    }
}
