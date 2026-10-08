using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobSubcontractOperationsQuery(int JobId) : IRequest<List<SubcontractOperationResponseModel>>;

public class GetJobSubcontractOperationsHandler(AppDbContext db)
    : IRequestHandler<GetJobSubcontractOperationsQuery, List<SubcontractOperationResponseModel>>
{
    public async Task<List<SubcontractOperationResponseModel>> Handle(GetJobSubcontractOperationsQuery request, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == request.JobId)
            .Select(j => new { j.Id, j.PartId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found.");

        if (job.PartId is not int partId)
            return [];

        var partQuantity = await db.JobParts.AsNoTracking()
            .Where(jp => jp.JobId == job.Id && jp.PartId == partId)
            .SumAsync(jp => jp.Quantity, ct);
        var jobQuantity = partQuantity > 0m ? partQuantity : 1m;

        return await db.Operations.AsNoTracking()
            .Where(o => o.PartId == partId && o.IsSubcontract && o.SubcontractVendorId != null)
            .OrderBy(o => o.StepNumber)
            .Select(o => new SubcontractOperationResponseModel(
                o.Id,
                o.StepNumber,
                o.Title,
                o.SubcontractVendorId!.Value,
                o.SubcontractVendor!.CompanyName,
                o.SubcontractCost,
                o.SubcontractTurnTimeDays ?? o.SubcontractLeadTimeDays,
                jobQuantity))
            .ToListAsync(ct);
    }
}
