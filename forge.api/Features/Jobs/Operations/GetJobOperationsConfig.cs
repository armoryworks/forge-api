using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Operations;

public record GetJobOperationsConfigQuery : IRequest<JobOperationsConfigResponseModel>;

public class GetJobOperationsConfigHandler(IJobOperationService operations, AppDbContext db)
    : IRequestHandler<GetJobOperationsConfigQuery, JobOperationsConfigResponseModel>
{
    public const string ReasonGroupCode = "operation_reason";

    public async Task<JobOperationsConfigResponseModel> Handle(GetJobOperationsConfigQuery request, CancellationToken ct)
    {
        var reasons = await db.ReferenceData
            .AsNoTracking()
            .Where(r => r.GroupCode == ReasonGroupCode && r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Label)
            .Select(r => new OperationReasonResponseModel(r.Code, r.Label))
            .ToListAsync(ct);
        return new(await operations.IsTrackingEnabledAsync(ct), reasons);
    }
}
