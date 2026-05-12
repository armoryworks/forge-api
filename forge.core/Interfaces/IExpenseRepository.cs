using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IExpenseRepository
{
    Task<List<ExpenseResponseModel>> GetExpensesAsync(int? userId, ExpenseStatus? status, string? search, CancellationToken ct);
    Task<ExpenseResponseModel?> GetByIdAsync(int id, CancellationToken ct);
    Task<Expense?> FindAsync(int id, CancellationToken ct);
    Task AddAsync(Expense expense, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
