using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Interfaces;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Features.Customers;

public record PlaceCreditHoldCommand(int CustomerId, string Reason) : IRequest;

public class PlaceCreditHoldValidator : AbstractValidator<PlaceCreditHoldCommand>
{
    public PlaceCreditHoldValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class PlaceCreditHoldHandler(AppDbContext db, IClock clock) : IRequestHandler<PlaceCreditHoldCommand>
{
    public async Task Handle(PlaceCreditHoldCommand request, CancellationToken ct)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("PlaceCreditHold requires an authenticated caller.");

        customer.IsOnCreditHold = true;
        customer.CreditHoldReason = request.Reason;
        customer.CreditHoldAt = clock.UtcNow;
        customer.CreditHoldById = userId;

        db.LogActivityAt(
            "credit-hold-placed",
            $"Credit hold placed: {request.Reason}",
            ("Customer", customer.Id));

        await db.SaveChangesAsync(ct);
    }
}
