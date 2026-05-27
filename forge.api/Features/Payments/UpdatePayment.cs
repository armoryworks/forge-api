using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Api.Features.Payments;

// P06-5: payments were delete-only. This adds an amend path, gated by the
// admin-selectable payments.modification-policy and guarded against reducing the
// amount below what's already applied to invoices.
public record UpdatePaymentCommand(int Id, UpdatePaymentRequestModel Data) : IRequest<PaymentListItemModel>;

public class UpdatePaymentValidator : AbstractValidator<UpdatePaymentCommand>
{
    public UpdatePaymentValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Amount).GreaterThan(0);
        RuleFor(x => x.Data.Method).NotEmpty();
    }
}

public class UpdatePaymentHandler(IPaymentRepository repo, ICustomerRepository customerRepo, ISettingsService settings)
    : IRequestHandler<UpdatePaymentCommand, PaymentListItemModel>
{
    public async Task<PaymentListItemModel> Handle(UpdatePaymentCommand request, CancellationToken cancellationToken)
    {
        var policy = await settings.GetStringAsync(PaymentsSettings.ModificationPolicyKey, cancellationToken)
                     ?? PaymentsSettings.PolicyFull;
        if (policy == PaymentsSettings.PolicyLocked)
            throw new InvalidOperationException("Payment modifications are locked by the payment policy.");

        var payment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {request.Id} not found");

        var appliedTotal = payment.Applications.Sum(a => a.Amount);
        if (request.Data.Amount < appliedTotal)
            throw new InvalidOperationException(
                $"Cannot reduce the payment to {request.Data.Amount:C}: {appliedTotal:C} is already applied to invoices. Void it instead.");

        payment.Method = Enum.Parse<PaymentMethod>(request.Data.Method, ignoreCase: true);
        payment.Amount = request.Data.Amount;
        payment.PaymentDate = request.Data.PaymentDate;
        payment.ReferenceNumber = request.Data.ReferenceNumber;
        payment.Notes = request.Data.Notes;

        await repo.SaveChangesAsync(cancellationToken);

        var customer = await customerRepo.FindAsync(payment.CustomerId, cancellationToken);
        return new PaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.CustomerId, customer?.Name ?? string.Empty,
            payment.Method.ToString(), payment.Amount, appliedTotal,
            payment.Amount - appliedTotal, payment.PaymentDate,
            payment.ReferenceNumber, payment.CreatedAt);
    }
}
