using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Soft-deletes a budget line (stamps <c>DeletedAt</c>; the change tracker records
/// the actor and emits the "deleted" ActivityLog row). CAP-ACCT-FULLGL gated.
/// Clearing the row also frees the (book, account, year, month) slot on the
/// filtered unique index so it can be re-created.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record DeleteBudgetCommand(int Id) : IRequest;

public class DeleteBudgetHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteBudgetCommand>
{
    public async Task Handle(DeleteBudgetCommand request, CancellationToken ct)
    {
        var budget = await db.AcctBudgets.FirstOrDefaultAsync(b => b.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Budget {request.Id} not found.");

        budget.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
