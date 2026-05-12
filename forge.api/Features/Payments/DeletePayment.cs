using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Payments;

public record DeletePaymentCommand(int Id) : IRequest;

public class DeletePaymentHandler(IPaymentRepository repo)
    : IRequestHandler<DeletePaymentCommand>
{
    public async Task Handle(DeletePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {request.Id} not found");

        if (payment.Applications.Any())
            throw new InvalidOperationException("Cannot delete a payment with applied invoices. Remove applications first.");

        payment.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
