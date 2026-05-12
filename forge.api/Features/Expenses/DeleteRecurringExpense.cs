using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.Expenses;

public record DeleteRecurringExpenseCommand(int Id) : IRequest;

public class DeleteRecurringExpenseHandler(AppDbContext db) : IRequestHandler<DeleteRecurringExpenseCommand>
{
    public async Task Handle(DeleteRecurringExpenseCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.RecurringExpenses.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Recurring expense {request.Id} not found");

        entity.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
