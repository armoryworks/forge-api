using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record EmployeeComplianceItem(
    int Id,
    string FormName,
    string FormType,
    string Status,
    DateTimeOffset? SignedAt,
    DateTimeOffset CreatedAt);

public record GetEmployeeComplianceQuery(int EmployeeId) : IRequest<List<EmployeeComplianceItem>>;

public class GetEmployeeComplianceHandler(AppDbContext db)
    : IRequestHandler<GetEmployeeComplianceQuery, List<EmployeeComplianceItem>>
{
    public async Task<List<EmployeeComplianceItem>> Handle(GetEmployeeComplianceQuery request, CancellationToken cancellationToken)
    {
        return await db.ComplianceFormSubmissions
            .Include(s => s.Template)
            .AsNoTracking()
            .Where(s => s.UserId == request.EmployeeId && s.DeletedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new EmployeeComplianceItem(
                s.Id,
                s.Template.Name,
                s.Template.FormType.ToString(),
                s.Status.ToString(),
                s.SignedAt,
                s.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
