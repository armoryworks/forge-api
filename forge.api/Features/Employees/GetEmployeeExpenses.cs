using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

public record EmployeeExpenseItem(
    int Id,
    DateTimeOffset ExpenseDate,
    string Category,
    string Description,
    decimal Amount,
    string Status,
    DateTimeOffset CreatedAt);

public record GetEmployeeExpensesQuery(int EmployeeId) : IRequest<List<EmployeeExpenseItem>>;

public class GetEmployeeExpensesHandler(AppDbContext db)
    : IRequestHandler<GetEmployeeExpensesQuery, List<EmployeeExpenseItem>>
{
    public async Task<List<EmployeeExpenseItem>> Handle(GetEmployeeExpensesQuery request, CancellationToken cancellationToken)
    {
        return await db.Expenses
            .AsNoTracking()
            .Where(e => e.UserId == request.EmployeeId && e.DeletedAt == null)
            .OrderByDescending(e => e.ExpenseDate)
            .Select(e => new EmployeeExpenseItem(
                e.Id,
                e.ExpenseDate,
                e.Category,
                e.Description,
                e.Amount,
                e.Status.ToString(),
                e.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
