using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentRuns;
using Forge.Api.Validation;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorPayments;

/// <summary>
/// Pays the bills picked from the payment run as a persisted <see cref="Forge.Core.Entities.PaymentRun"/> (see
/// <see cref="PaymentRuns.CreatePaymentRunHandler"/>): one vendor payment per vendor and currency, each through the
/// normal vendor-payment create. Every pick is checked before the first payment is created, so a bad pick refuses the
/// whole run. A vendor failing part-way interrupts the run: this endpoint then refuses with the reason while the
/// vendors already paid stay paid, and the run can be resumed from the payment runs list. Vendor credits are not
/// deducted here; the payment-runs endpoint does that.
/// </summary>
public record CreateVendorPaymentsFromSuggestionsCommand(CreateVendorPaymentsFromSuggestionsRequestModel Request)
    : IRequest<List<VendorPaymentListItemModel>>;

public class CreateVendorPaymentsFromSuggestionsValidator : AbstractValidator<CreateVendorPaymentsFromSuggestionsCommand>
{
    public CreateVendorPaymentsFromSuggestionsValidator()
    {
        RuleFor(x => x.Request.Items).NotEmpty().WithMessage("Pick at least one bill to pay.");
        RuleFor(x => x.Request.Method).NotEmpty()
            .Must(m => Enum.TryParse<PaymentMethod>(m, ignoreCase: true, out _))
            .WithMessage("Method must be one of: Cash, Check, CreditCard, BankTransfer, Wire, Other");
        RuleFor(x => x.Request.Items)
            .Must(items => items.Select(i => i.VendorBillId).Distinct().Count() == items.Count)
            .When(x => x.Request.Items is { Count: > 0 })
            .WithMessage("A bill may be picked at most once.");
        RuleForEach(x => x.Request.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.VendorBillId).GreaterThan(0);
            item.RuleFor(i => i.Amount).GreaterThan(0m).MoneyScale();
            item.RuleFor(i => i.DiscountAmount).GreaterThanOrEqualTo(0m).MoneyScale().When(i => i.DiscountAmount.HasValue);
        });
    }
}

public class CreateVendorPaymentsFromSuggestionsHandler(AppDbContext db, ISender sender, IClock clock)
    : IRequestHandler<CreateVendorPaymentsFromSuggestionsCommand, List<VendorPaymentListItemModel>>
{
    public async Task<List<VendorPaymentListItemModel>> Handle(
        CreateVendorPaymentsFromSuggestionsCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var run = await PaymentRunFactory.CreateAsync(db, new CreatePaymentRunRequestModel(
            request.PaymentDate, request.Method, request.Items, request.ReferenceNumber, request.Notes,
            ApplyVendorCredits: false), cancellationToken);
        var payments = await new PaymentRunExecutor(db, sender, clock).ExecuteAsync(run.Id, cancellationToken);

        var outcome = await db.PaymentRuns.AsNoTracking()
            .Where(r => r.Id == run.Id)
            .Select(r => new { r.Status, r.LastError })
            .FirstAsync(cancellationToken);
        if (outcome.Status == PaymentRunStatus.Interrupted)
            throw new InvalidOperationException(
                $"{outcome.LastError} Payment run {run.RunNumber} was interrupted; the vendors already paid stay paid. Resume the run to pay the rest.");
        return payments;
    }
}
