using MediatR;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Customers;

public record DeleteCustomerCommand(int Id) : IRequest;

public class DeleteCustomerHandler(ICustomerRepository repo, AppDbContext db, IClock clock)
    : IRequestHandler<DeleteCustomerCommand>
{
    public async Task Handle(DeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.Id} not found");

        customer.DeletedAt = clock.UtcNow;
        // DeletedBy is auto-stamped by AppDbContext.SetTimestamps.

        var displayLabel = string.IsNullOrWhiteSpace(customer.CompanyName)
            ? customer.Name
            : $"{customer.Name} ({customer.CompanyName})";
        db.LogActivityAt(
            "deleted",
            $"Deleted customer: {displayLabel}",
            ("Customer", customer.Id));

        await repo.SaveChangesAsync(cancellationToken);
    }
}
