using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Interfaces;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Customers;

public record ReleaseCreditHoldCommand(int CustomerId) : IRequest;

public class ReleaseCreditHoldHandler(AppDbContext db, IClock clock) : IRequestHandler<ReleaseCreditHoldCommand>
{
    public async Task Handle(ReleaseCreditHoldCommand request, CancellationToken ct)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var priorReason = customer.CreditHoldReason;

        customer.IsOnCreditHold = false;
        customer.CreditHoldReason = null;
        customer.CreditHoldAt = null;
        customer.CreditHoldById = null;
        customer.LastCreditReviewDate = clock.UtcNow;

        db.LogActivityAt(
            "credit-hold-released",
            string.IsNullOrEmpty(priorReason)
                ? "Credit hold released"
                : $"Credit hold released (was: {priorReason})",
            ("Customer", customer.Id));

        await db.SaveChangesAsync(ct);
    }
}
