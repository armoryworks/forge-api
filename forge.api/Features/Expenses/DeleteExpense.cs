using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Middleware;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Expenses;

public sealed record DeleteExpenseCommand(int Id) : IRequest;

public sealed class DeleteExpenseHandler(IExpenseRepository repo, IHttpContextAccessor http)
    : IRequestHandler<DeleteExpenseCommand>
{
    public async Task Handle(DeleteExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Expense {request.Id} not found");

        // F-EXP-06: only the owner — or an approver role — may delete an expense.
        var user = http.HttpContext?.User;
        var callerId = int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
        var isApprover = user?.IsInRole("Admin") == true
                         || user?.IsInRole("Manager") == true
                         || user?.IsInRole("OfficeManager") == true;
        if (expense.UserId != callerId && !isApprover)
            throw new ForbiddenException("You can only delete your own expenses.");

        if (expense.Status != ExpenseStatus.Pending)
            throw new InvalidOperationException("Only pending expenses can be deleted.");

        expense.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
