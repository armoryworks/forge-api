using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record EmployeePayStubItem(
    int Id,
    DateTimeOffset PayPeriodStart,
    DateTimeOffset PayPeriodEnd,
    DateTimeOffset PayDate,
    decimal GrossPay,
    decimal NetPay,
    decimal TotalDeductions,
    decimal TotalTaxes);

public record GetEmployeePaySummaryQuery(int EmployeeId) : IRequest<List<EmployeePayStubItem>>;

public class GetEmployeePaySummaryHandler(AppDbContext db)
    : IRequestHandler<GetEmployeePaySummaryQuery, List<EmployeePayStubItem>>
{
    public async Task<List<EmployeePayStubItem>> Handle(GetEmployeePaySummaryQuery request, CancellationToken cancellationToken)
    {
        return await db.PayStubs
            .AsNoTracking()
            .Where(p => p.UserId == request.EmployeeId && p.DeletedAt == null)
            .OrderByDescending(p => p.PayDate)
            .Select(p => new EmployeePayStubItem(
                p.Id,
                p.PayPeriodStart,
                p.PayPeriodEnd,
                p.PayDate,
                p.GrossPay,
                p.NetPay,
                p.TotalDeductions,
                p.TotalTaxes))
            .ToListAsync(cancellationToken);
    }
}
