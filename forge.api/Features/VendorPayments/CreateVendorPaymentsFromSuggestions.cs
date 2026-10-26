using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentTerms;
using Forge.Api.Services;
using Forge.Api.Validation;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.VendorPayments;

/// <summary>
/// Pays the bills picked from the payment run: one <see cref="CreateVendorPaymentCommand"/> per vendor and
/// currency, so segregation of duties, the cash posting and ACH batching work exactly as for a payment entered by
/// hand. Every pick (status, discount and cash plus discount against the balance) is checked before the first
/// payment is created, so a bad pick refuses the whole run instead of leaving some vendors paid.
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

public class CreateVendorPaymentsFromSuggestionsHandler(AppDbContext db, ISender sender)
    : IRequestHandler<CreateVendorPaymentsFromSuggestionsCommand, List<VendorPaymentListItemModel>>
{
    public async Task<List<VendorPaymentListItemModel>> Handle(
        CreateVendorPaymentsFromSuggestionsCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var billIds = request.Items.Select(i => i.VendorBillId).ToList();
        var bills = await db.VendorBills.AsNoTracking()
            .Include(b => b.Lines)
            .Include(b => b.PaymentApplications)
            .Where(b => billIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var missing = billIds.Where(id => !bills.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new KeyNotFoundException($"Vendor bill {missing[0]} not found");

        var notPayable = bills.Values.FirstOrDefault(b => b.Status is not (VendorBillStatus.Approved or VendorBillStatus.PartiallyPaid));
        if (notPayable is not null)
            throw new InvalidOperationException(
                $"Vendor bill {notPayable.BillNumber} is {notPayable.Status}; only Approved or PartiallyPaid bills can be paid");

        var discounts = await DiscountSettings.ReadAsync(db, cancellationToken);
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var bill = bills[item.VendorBillId];
            var discount = EarlyPaymentDiscountRules.ForBill(
                db, bill, item.Amount, request.PaymentDate, item.DiscountAmount, false, null, discounts,
                $"Request.Items[{index}].DiscountAmount");
            if (item.Amount + discount > bill.BalanceDue)
            {
                var money = await MoneyFormat.ForCurrencyIdAsync(db, bill.CurrencyId, cancellationToken);
                throw new InvalidOperationException(
                    $"Application amount {money.Format(item.Amount)} plus discount {money.Format(discount)} exceeds bill {bill.BillNumber} balance of {money.Format(bill.BalanceDue)}");
            }
        }

        var results = new List<VendorPaymentListItemModel>();
        foreach (var group in request.Items
                     .GroupBy(i => (bills[i.VendorBillId].VendorId, bills[i.VendorBillId].CurrencyId))
                     .OrderBy(g => g.Key.VendorId).ThenBy(g => g.Key.CurrencyId))
        {
            var applications = group
                .Select(i => new CreateVendorPaymentApplicationModel(i.VendorBillId, i.Amount, DiscountAmount: i.DiscountAmount))
                .ToList();
            results.Add(await sender.Send(new CreateVendorPaymentCommand(
                group.Key.VendorId, request.Method, applications.Sum(a => a.Amount), request.PaymentDate,
                request.ReferenceNumber, request.Notes, applications, CurrencyId: group.Key.CurrencyId), cancellationToken));
        }

        return results;
    }
}
