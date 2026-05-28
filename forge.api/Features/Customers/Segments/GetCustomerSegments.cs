using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Customers.Segments;

// C3: list the persisted customer segments (replaces the page's hard-coded examples).
public record GetCustomerSegmentsQuery : IRequest<List<CustomerSegmentResponseModel>>;

public class GetCustomerSegmentsHandler(AppDbContext db) : IRequestHandler<GetCustomerSegmentsQuery, List<CustomerSegmentResponseModel>>
{
    public async Task<List<CustomerSegmentResponseModel>> Handle(GetCustomerSegmentsQuery request, CancellationToken ct)
        => await db.CustomerSegments
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new CustomerSegmentResponseModel(s.Id, s.Name, s.Description, s.FilterCriteria, s.IsActive, s.CreatedAt))
            .ToListAsync(ct);
}
