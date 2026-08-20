using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Extensions;

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

public class UpdatePaymentHandler(
    IPaymentRepository repo,
    ICustomerRepository customerRepo,
    ISettingsService settings,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db)
    : IRequestHandler<UpdatePaymentCommand, PaymentListItemModel>
{
    // System setting that gates caller-supplied payment numbers (shared with CreatePayment).
    private const string AllowManualPaymentNumbersKey = "payments.allow_manual_numbers";

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

        // User-settable payment number — audit-sensitive, so STRICTLY gated: a payment that has
        // been applied to any invoice is committed and its number is frozen. Payment has no status/
        // lifecycle field, so "no applications yet" is the strictest sensible "hasn't moved" condition
        // (it also mirrors the applied-amount guard above). The gate is enforced BEFORE the setting/
        // uniqueness checks so a posted document is rejected regardless of configuration.
        var numberChanged = false;
        if (request.Data.PaymentNumber is not null)
        {
            var newNumber = request.Data.PaymentNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, payment.PaymentNumber, StringComparison.Ordinal))
            {
                if (payment.Applications.Count > 0)
                    throw new InvalidOperationException(
                        "A payment's number can only be changed before it has been applied.");
                if (!await ManualPaymentNumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Manual payment numbers are disabled. Turn on 'payments.allow_manual_numbers' in settings to change a payment number.");
                if (await repo.PaymentNumberExistsAsync(newNumber, payment.Id, cancellationToken))
                    throw new InvalidOperationException($"Payment number '{newNumber}' is already in use.");
                // Record the rename in the identifier registry: ensure the current number is on record
                // (covers pre-registry payments), then supersede it — the old number stays resolvable.
                await identifiers.IssueAsync(BusinessEntityType.Payment, payment.Id, payment.PaymentNumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.Payment, payment.Id, newNumber, cancellationToken);
                payment.PaymentNumber = newNumber;
                numberChanged = true;
            }
        }

        payment.Method = Enum.Parse<PaymentMethod>(request.Data.Method, ignoreCase: true);
        payment.Amount = request.Data.Amount;
        payment.PaymentDate = request.Data.PaymentDate;
        payment.ReferenceNumber = request.Data.ReferenceNumber;
        payment.Notes = request.Data.Notes;

        if (numberChanged)
            db.LogActivityAt("updated", $"Changed payment number to {payment.PaymentNumber}", ("Payment", payment.Id));

        await repo.SaveChangesAsync(cancellationToken);

        var customer = await customerRepo.FindAsync(payment.CustomerId, cancellationToken);
        return new PaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.CustomerId, customer?.Name ?? string.Empty,
            payment.Method.ToString(), payment.Amount, appliedTotal,
            payment.Amount - appliedTotal, payment.PaymentDate,
            payment.ReferenceNumber, payment.CreatedAt);
    }

    private async Task<bool> ManualPaymentNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualPaymentNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
