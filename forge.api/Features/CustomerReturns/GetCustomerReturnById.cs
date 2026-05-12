using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerReturns;

public record GetCustomerReturnByIdQuery(int Id) : IRequest<CustomerReturnDetailResponseModel>;

public class GetCustomerReturnByIdHandler(AppDbContext db)
    : IRequestHandler<GetCustomerReturnByIdQuery, CustomerReturnDetailResponseModel>
{
    public async Task<CustomerReturnDetailResponseModel> Handle(GetCustomerReturnByIdQuery request, CancellationToken ct)
    {
        var r = await db.CustomerReturns
            .Include(r => r.Customer)
            .Include(r => r.OriginalJob)
            .Include(r => r.ReworkJob)
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Customer return {request.Id} not found");

        string? inspectedByName = null;
        if (r.InspectedById.HasValue)
        {
            var user = await db.Users.FindAsync([r.InspectedById.Value], ct);
            inspectedByName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null;
        }

        return new CustomerReturnDetailResponseModel(
            r.Id, r.ReturnNumber, r.CustomerId, r.Customer.Name,
            r.OriginalJobId, r.OriginalJob.JobNumber, r.OriginalJob.Title,
            r.ReworkJobId, r.ReworkJob?.JobNumber,
            r.Status.ToString(), r.Reason, r.Notes, r.ReturnDate,
            r.InspectedById, inspectedByName, r.InspectedAt, r.InspectionNotes,
            r.CreatedAt, r.UpdatedAt);
    }
}
