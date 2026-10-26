using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Expenses;

/// <summary>
/// F-EXP-03: mark an approved expense reimbursed — the terminal state once the approved amount has
/// been paid back (syncs to AP/QBO downstream). Accounting-role action; only an Approved /
/// SelfApproved expense can transition (else 409), so a second reimbursement is refused. With
/// CAP-ACCT-FULLGL on, an out-of-pocket expense's payout posts Dr Employee Reimbursements Payable /
/// Cr Cash in the same transaction as the status change.
/// </summary>
public record ReimburseExpenseCommand(int Id) : IRequest;

public class ReimburseExpenseHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor? httpContext = null,
    IExpenseApPostingService? apPosting = null) : IRequestHandler<ReimburseExpenseCommand>
{
    public async Task Handle(ReimburseExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Expense not found.");

        if (expense.Status == ExpenseStatus.Reimbursed)
            throw new InvalidOperationException("This expense has already been reimbursed.");

        if (expense.Status is not (ExpenseStatus.Approved or ExpenseStatus.SelfApproved))
            throw new InvalidOperationException("Only an approved expense can be marked reimbursed.");

        var actorId = int.TryParse(
            httpContext?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : expense.ApprovedBy ?? expense.UserId;

        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        expense.Status = ExpenseStatus.Reimbursed;
        db.LogActivityAt("expense-reimbursed", "Expense marked reimbursed", ("Expense", expense.Id));
        await db.SaveChangesAsync(cancellationToken);

        if (apPosting is not null)
            await apPosting.PostExpenseReimbursedAsync(
                expense.Id, actorId, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), cancellationToken);

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);
    }
}
