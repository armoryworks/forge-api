using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record GetPendingSubcontractsQuery : IRequest<List<PendingSubcontractResponseModel>>;

public class GetPendingSubcontractsHandler(AppDbContext db) : IRequestHandler<GetPendingSubcontractsQuery, List<PendingSubcontractResponseModel>>
{
    public async Task<List<PendingSubcontractResponseModel>> Handle(GetPendingSubcontractsQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var orders = await db.SubcontractOrders
            .AsNoTracking()
            .Include(o => o.Job)
            .Include(o => o.Operation)
            .Include(o => o.Vendor)
            .Where(o => o.Status != SubcontractStatus.Complete && o.Status != SubcontractStatus.Rejected)
            .OrderBy(o => o.ExpectedReturnDate ?? DateTimeOffset.MaxValue)
            .Select(o => new PendingSubcontractResponseModel
            {
                Id = o.Id,
                JobId = o.JobId,
                JobNumber = o.Job.JobNumber ?? $"J-{o.Job.Id}",
                OperationId = o.OperationId,
                OperationName = o.Operation.Title,
                VendorId = o.VendorId,
                VendorName = o.Vendor.CompanyName,
                Quantity = o.Quantity,
                SentAt = o.SentAt,
                ExpectedReturnDate = o.ExpectedReturnDate,
                Status = o.Status.ToString(),
                IsOverdue = o.ExpectedReturnDate.HasValue && o.ExpectedReturnDate.Value < now,
                DaysOut = (int)(now - o.SentAt).TotalDays,
            })
            .ToListAsync(ct);

        return orders;
    }
}
