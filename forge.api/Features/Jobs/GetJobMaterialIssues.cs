using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobMaterialIssuesQuery(int JobId, int Page = 1, int PageSize = 25)
    : IRequest<List<MaterialIssueResponseModel>>;

public class GetJobMaterialIssuesHandler(AppDbContext db)
    : IRequestHandler<GetJobMaterialIssuesQuery, List<MaterialIssueResponseModel>>
{
    public async Task<List<MaterialIssueResponseModel>> Handle(
        GetJobMaterialIssuesQuery request, CancellationToken cancellationToken)
    {
        return await db.MaterialIssues
            .AsNoTracking()
            .Where(m => m.JobId == request.JobId)
            .OrderByDescending(m => m.IssuedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new MaterialIssueResponseModel
            {
                Id = m.Id,
                JobId = m.JobId,
                PartId = m.PartId,
                PartNumber = m.Part.PartNumber,
                PartDescription = m.Part.Description ?? string.Empty,
                OperationId = m.OperationId,
                OperationName = m.Operation != null ? m.Operation.Title : null,
                Quantity = m.Quantity,
                UnitCost = m.UnitCost,
                TotalCost = m.Quantity * m.UnitCost,
                IssuedByName = string.Empty,
                IssuedAt = m.IssuedAt,
                LotNumber = m.LotNumber,
                IssueType = m.IssueType,
                Notes = m.Notes,
            })
            .ToListAsync(cancellationToken);
    }
}
