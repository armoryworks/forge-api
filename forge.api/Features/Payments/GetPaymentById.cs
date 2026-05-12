using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Payments;

public record GetPaymentByIdQuery(int Id) : IRequest<PaymentDetailResponseModel>;

public class GetPaymentByIdHandler(IPaymentRepository repo)
    : IRequestHandler<GetPaymentByIdQuery, PaymentDetailResponseModel>
{
    public async Task<PaymentDetailResponseModel> Handle(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {request.Id} not found");

        var appliedAmount = payment.Applications.Sum(a => a.Amount);

        return new PaymentDetailResponseModel(
            payment.Id,
            payment.PaymentNumber,
            payment.CustomerId,
            payment.Customer.Name,
            payment.Method.ToString(),
            payment.Amount,
            appliedAmount,
            payment.Amount - appliedAmount,
            payment.PaymentDate,
            payment.ReferenceNumber,
            payment.Notes,
            payment.Applications.Select(a => new PaymentApplicationResponseModel(
                a.Id, a.PaymentId, payment.PaymentNumber,
                a.InvoiceId, a.Invoice.InvoiceNumber, a.Amount)).ToList(),
            payment.CreatedAt,
            payment.UpdatedAt);
    }
}
