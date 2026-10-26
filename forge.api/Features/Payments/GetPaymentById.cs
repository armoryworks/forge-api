using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.Payments;

public record GetPaymentByIdQuery(int Id) : IRequest<PaymentDetailResponseModel>;

public class GetPaymentByIdHandler(IPaymentRepository repo, ISettingsService? settings = null, AppDbContext? db = null)
    : IRequestHandler<GetPaymentByIdQuery, PaymentDetailResponseModel>
{
    public async Task<PaymentDetailResponseModel> Handle(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var payment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {request.Id} not found");

        var appliedAmount = payment.Applications.Sum(a => a.Amount);
        var policy = settings is null
            ? null
            : await settings.GetStringAsync(PaymentsSettings.ModificationPolicyKey, cancellationToken);

        return new PaymentDetailResponseModel(
            payment.Id,
            payment.PaymentNumber,
            payment.CustomerId,
            payment.Customer.Name,
            payment.Method.ToString(),
            payment.Amount,
            appliedAmount,
            payment.UnappliedAmount,
            payment.PaymentDate,
            payment.ReferenceNumber,
            payment.Notes,
            payment.Applications.Select(a => new PaymentApplicationResponseModel(
                a.Id, a.PaymentId, payment.PaymentNumber,
                a.InvoiceId, a.Invoice.InvoiceNumber, a.Amount, a.DiscountAmount)).ToList(),
            payment.CreatedAt,
            payment.UpdatedAt,
            payment.Version,
            CanVoid: (policy ?? PaymentsSettings.PolicyFull) == PaymentsSettings.PolicyFull,
            CurrencyId: payment.CurrencyId,
            CurrencyCode: db is not null && payment.CurrencyId is int currencyId
                ? await db.Currencies.AsNoTracking().Where(c => c.Id == currencyId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken)
                : null,
            FxRate: payment.FxRate,
            RefundedAmount: payment.RefundedAmount,
            Refunds: payment.Refunds.OrderBy(r => r.RefundDate)
                .Select(r => new CustomerRefundModel(
                    r.Id, r.RefundNumber, r.CustomerId, r.Amount, r.Method.ToString(), r.RefundDate, r.Reference,
                    r.Status.ToString(), null, null, payment.Id, payment.PaymentNumber, null, r.FxRate))
                .ToList());
    }
}
