using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class ExpenseRepository(AppDbContext db) : IExpenseRepository
{
    public async Task<List<ExpenseResponseModel>> GetExpensesAsync(int? userId, ExpenseStatus? status, string? search, CancellationToken ct)
    {
        var query = db.Expenses.Include(e => e.Job).AsQueryable();

        if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e =>
                e.Description.ToLower().Contains(term) ||
                e.Category.ToLower().Contains(term));
        }

        var expenses = await query
            .OrderByDescending(e => e.ExpenseDate)
            .ToListAsync(ct);

        var userIds = expenses.Select(e => e.UserId)
            .Concat(expenses.Where(e => e.ApprovedBy.HasValue).Select(e => e.ApprovedBy!.Value))
            .Distinct().ToList();

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return expenses.Select(e => ToResponseModel(e, users)).ToList();
    }

    public async Task<ExpenseResponseModel?> GetByIdAsync(int id, CancellationToken ct)
    {
        var expense = await db.Expenses.Include(e => e.Job)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (expense is null) return null;

        var userIds = new List<int> { expense.UserId };
        if (expense.ApprovedBy.HasValue) userIds.Add(expense.ApprovedBy.Value);

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return ToResponseModel(expense, users);
    }

    public Task<Expense?> FindAsync(int id, CancellationToken ct)
        => db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(Expense expense, CancellationToken ct)
    {
        await db.Expenses.AddAsync(expense, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);

    private static ExpenseResponseModel ToResponseModel(Expense e, Dictionary<int, ApplicationUser> users)
    {
        var userName = users.TryGetValue(e.UserId, out var user)
            ? $"{user.FirstName} {user.LastName}" : "Unknown";
        var approvedByName = e.ApprovedBy.HasValue && users.TryGetValue(e.ApprovedBy.Value, out var approver)
            ? $"{approver.FirstName} {approver.LastName}" : null;

        return new ExpenseResponseModel(
            e.Id, e.UserId, userName, e.JobId, e.Job?.JobNumber,
            e.Amount, e.Category, e.Description, e.ReceiptFileId,
            e.Status, e.ApprovedBy, approvedByName, e.ApprovalNotes,
            e.ExpenseDate, e.CreatedAt);
    }
}
