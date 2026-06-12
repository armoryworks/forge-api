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
            .Include(e => e.Vendor)
            .OrderByDescending(e => e.ExpenseDate)
            .ToListAsync(ct);

        var userIds = expenses.Select(e => e.UserId)
            .Concat(expenses.Where(e => e.ApprovedBy.HasValue).Select(e => e.ApprovedBy!.Value))
            .Distinct().ToList();

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var promotedBills = await GetLivePromotedBillsAsync(expenses.Select(e => e.Id).ToList(), ct);

        return expenses.Select(e => ToResponseModel(e, users, promotedBills)).ToList();
    }

    public async Task<ExpenseResponseModel?> GetByIdAsync(int id, CancellationToken ct)
    {
        var expense = await db.Expenses.Include(e => e.Job)
            .Include(e => e.Vendor)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (expense is null) return null;

        var userIds = new List<int> { expense.UserId };
        if (expense.ApprovedBy.HasValue) userIds.Add(expense.ApprovedBy.Value);

        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var promotedBills = await GetLivePromotedBillsAsync([expense.Id], ct);

        return ToResponseModel(expense, users, promotedBills);
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

    /// <summary>
    /// The live (non-void) promoted bill per expense id — (billId, billNumber) keyed by ExpenseId.
    /// One grouped lookup for the list path (no N+1).
    /// </summary>
    private async Task<Dictionary<int, (int BillId, string BillNumber)>> GetLivePromotedBillsAsync(
        IReadOnlyCollection<int> expenseIds, CancellationToken ct)
    {
        if (expenseIds.Count == 0) return [];

        return (await db.VendorBills.AsNoTracking()
                .Where(b => b.ExpenseId != null
                    && expenseIds.Contains(b.ExpenseId.Value)
                    && b.Status != VendorBillStatus.Void)
                .Select(b => new { ExpenseId = b.ExpenseId!.Value, b.Id, b.BillNumber })
                .ToListAsync(ct))
            .ToDictionary(b => b.ExpenseId, b => (b.Id, b.BillNumber));
    }

    private static ExpenseResponseModel ToResponseModel(
        Expense e,
        Dictionary<int, ApplicationUser> users,
        Dictionary<int, (int BillId, string BillNumber)> promotedBills)
    {
        var userName = users.TryGetValue(e.UserId, out var user)
            ? $"{user.FirstName} {user.LastName}" : "Unknown";
        var approvedByName = e.ApprovedBy.HasValue && users.TryGetValue(e.ApprovedBy.Value, out var approver)
            ? $"{approver.FirstName} {approver.LastName}" : null;
        var promotedBill = promotedBills.TryGetValue(e.Id, out var bill)
            ? bill
            : ((int BillId, string BillNumber)?)null;

        return new ExpenseResponseModel(
            e.Id, e.UserId, userName, e.JobId, e.Job?.JobNumber,
            e.Amount, e.Category, e.Description, e.ReceiptFileId,
            e.Status, e.ApprovedBy, approvedByName, e.ApprovalNotes,
            e.ExpenseDate, e.CreatedAt,
            e.VendorId, e.Vendor?.CompanyName,
            promotedBill?.BillId, promotedBill?.BillNumber);
    }
}
