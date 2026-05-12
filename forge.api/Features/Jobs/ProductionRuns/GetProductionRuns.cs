using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

public record GetProductionRunsQuery(int JobId) : IRequest<List<ProductionRunResponseModel>>;

public class GetProductionRunsHandler(AppDbContext db) : IRequestHandler<GetProductionRunsQuery, List<ProductionRunResponseModel>>
{
    public async Task<List<ProductionRunResponseModel>> Handle(GetProductionRunsQuery request, CancellationToken cancellationToken)
    {
        var runs = await db.ProductionRuns
            .Where(pr => pr.JobId == request.JobId)
            .Include(pr => pr.Job)
            .Include(pr => pr.Part)
            .ToListAsync(cancellationToken);

        var operatorIds = runs
            .Where(r => r.OperatorId.HasValue)
            .Select(r => r.OperatorId!.Value)
            .Distinct()
            .ToList();

        var operators = operatorIds.Count > 0
            ? await db.Users
                .Where(u => operatorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken)
            : new Dictionary<int, string>();

        return runs.Select(pr => new ProductionRunResponseModel(
            pr.Id,
            pr.JobId,
            pr.Job.JobNumber,
            pr.PartId,
            pr.Part.PartNumber,
            pr.Part.Description ?? pr.Part.Name,
            pr.OperatorId,
            pr.OperatorId.HasValue && operators.TryGetValue(pr.OperatorId.Value, out var name) ? name : null,
            pr.RunNumber,
            pr.TargetQuantity,
            pr.CompletedQuantity,
            pr.ScrapQuantity,
            pr.Status.ToString(),
            pr.StartedAt,
            pr.CompletedAt,
            pr.Notes,
            pr.SetupTimeMinutes,
            pr.RunTimeMinutes,
            pr.CompletedQuantity > 0
                ? (pr.CompletedQuantity - pr.ScrapQuantity) * 100.0m / pr.CompletedQuantity
                : 0m
        )).ToList();
    }
}
