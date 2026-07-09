using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Expenses;

/// <summary>
/// F-EXP-03: mark an approved expense reimbursed — the terminal state once the approved amount has
/// been paid back (syncs to AP/QBO downstream). Accounting-role action; only an Approved /
/// SelfApproved expense can transition (else 409).
/// </summary>
public record ReimburseExpenseCommand(int Id) : IRequest;

public class ReimburseExpenseHandler(AppDbContext db) : IRequestHandler<ReimburseExpenseCommand>
{
    public async Task Handle(ReimburseExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Expense not found.");

        if (expense.Status is not (ExpenseStatus.Approved or ExpenseStatus.SelfApproved))
            throw new InvalidOperationException("Only an approved expense can be marked reimbursed.");

        expense.Status = ExpenseStatus.Reimbursed;
        db.LogActivityAt("expense-reimbursed", "Expense marked reimbursed", ("Expense", expense.Id));
        await db.SaveChangesAsync(cancellationToken);
    }
}
