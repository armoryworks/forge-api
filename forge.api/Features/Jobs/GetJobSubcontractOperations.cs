using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetJobSubcontractOperationsQuery(int JobId) : IRequest<List<SubcontractOperationResponseModel>>;

public class GetJobSubcontractOperationsHandler(AppDbContext db, IJobOperationService operations)
    : IRequestHandler<GetJobSubcontractOperationsQuery, List<SubcontractOperationResponseModel>>
{
    public async Task<List<SubcontractOperationResponseModel>> Handle(GetJobSubcontractOperationsQuery request, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found.");

        if (job.PartId is not int partId)
            return [];

        var room = await SubcontractSendLimit.ForJobAsync(db, operations, job, ct);
        var jobQuantity = room.StartQuantity;

        var steps = await db.Operations.AsNoTracking()
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

        return steps
            .Select(s => s with
            {
                CommittedQuantity = room.CommittedFor(s.OperationId),
                RemainingQuantity = room.RemainingFor(s.OperationId),
                MaxSendQuantity = room.MaxSendFor(s.OperationId),
            })
            .ToList();
    }
}
