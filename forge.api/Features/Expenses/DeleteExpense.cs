using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Expenses;

public sealed record DeleteExpenseCommand(int Id) : IRequest;

public sealed class DeleteExpenseHandler(IExpenseRepository repo)
    : IRequestHandler<DeleteExpenseCommand>
{
    public async Task Handle(DeleteExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Expense {request.Id} not found");

        if (expense.Status != ExpenseStatus.Pending)
            throw new InvalidOperationException("Only pending expenses can be deleted.");

        expense.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
