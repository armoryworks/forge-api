using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PaymentTerms;
using Forge.Api.Features.VendorCredits;
using Forge.Api.Services;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.PaymentRuns;

public record CreatePaymentRunCommand(CreatePaymentRunRequestModel Request) : IRequest<PaymentRunDetailResponseModel>;

public class CreatePaymentRunValidator : AbstractValidator<CreatePaymentRunCommand>
{
    public CreatePaymentRunValidator()
    {
        RuleFor(x => x.Request.Items).NotEmpty().WithMessage("Pick at least one bill to pay.");
        RuleFor(x => x.Request.Method).NotEmpty()
            .Must(m => Enum.TryParse<PaymentMethod>(m, ignoreCase: true, out _))
            .WithMessage("Method must be one of: Cash, Check, CreditCard, BankTransfer, Wire, Other");
        RuleFor(x => x.Request.ReferenceNumber).MaximumLength(100);
        RuleFor(x => x.Request.Notes).MaximumLength(2000);
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

/// <summary>
/// Records a payment run (see <see cref="PaymentRunFactory"/>) and pays it (see <see cref="PaymentRunExecutor"/>). A
/// vendor failing part-way leaves the run Interrupted, which the response shows; resuming it pays the rest.
/// </summary>
public class CreatePaymentRunHandler(AppDbContext db, PaymentRunExecutor executor, IMediator mediator)
    : IRequestHandler<CreatePaymentRunCommand, PaymentRunDetailResponseModel>
{
    public async Task<PaymentRunDetailResponseModel> Handle(CreatePaymentRunCommand command, CancellationToken ct)
    {
        var run = await PaymentRunFactory.CreateAsync(db, command.Request, ct);
        await executor.ExecuteAsync(run.Id, ct);
        return await mediator.Send(new GetPaymentRunByIdQuery(run.Id), ct);
    }
}

/// <summary>
/// Records a payment run, unpaid. Every pick is checked first (status, the
/// discount, and cash plus discount against the balance), so a bad pick refuses the run before anything is saved. The
/// discount of each pick is fixed when the run is recorded. With <c>ApplyVendorCredits</c> each vendor's open credit
/// memos in the bill's currency are deducted from the picked cash, oldest first; when the credits cover a vendor's
/// whole run no payment is made, so no discount is taken and the credits settle the full balance instead.
/// </summary>
public static class PaymentRunFactory
{
    public static async Task<PaymentRun> CreateAsync(AppDbContext db, CreatePaymentRunRequestModel request, CancellationToken ct)
    {
        var billIds = request.Items.Select(i => i.VendorBillId).ToList();
        var bills = await db.VendorBills.AsNoTracking()
            .Include(b => b.Lines)
            .Include(b => b.PaymentApplications)
            .Include(b => b.CreditApplications)
            .Where(b => billIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);

        var missing = billIds.Where(id => !bills.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new KeyNotFoundException($"Vendor bill {missing[0]} not found");

        var notPayable = bills.Values.FirstOrDefault(b => b.Status is not (VendorBillStatus.Approved or VendorBillStatus.PartiallyPaid));
        if (notPayable is not null)
            throw new InvalidOperationException(
                $"Vendor bill {notPayable.BillNumber} is {notPayable.Status}; only Approved or PartiallyPaid bills can be paid");

        var run = new PaymentRun
        {
            PaymentDate = request.PaymentDate,
            Method = Enum.Parse<PaymentMethod>(request.Method, true).ToString(),
            ReferenceNumber = string.IsNullOrWhiteSpace(request.ReferenceNumber) ? null : request.ReferenceNumber.Trim(),
            Notes = request.Notes,
            ApplyVendorCredits = request.ApplyVendorCredits,
            CreatedByUserId = db.CurrentUserId,
            Status = PaymentRunStatus.Pending,
        };

        var discounts = await DiscountSettings.ReadAsync(db, ct);
        for (var index = 0; index < request.Items.Count; index++)
        {
            var pick = request.Items[index];
            var bill = bills[pick.VendorBillId];
            var discount = EarlyPaymentDiscountRules.ForBill(
                db, bill, pick.Amount, request.PaymentDate, pick.DiscountAmount, false, null, discounts,
                $"Request.Items[{index}].DiscountAmount");
            if (pick.Amount + discount > bill.BalanceDue)
            {
                var money = await MoneyFormat.ForCurrencyIdAsync(db, bill.CurrencyId, ct);
                throw new InvalidOperationException(
                    $"Application amount {money.Format(pick.Amount)} plus discount {money.Format(discount)} exceeds bill {bill.BillNumber} balance of {money.Format(bill.BalanceDue)}");
            }

            run.Items.Add(new PaymentRunItem
            {
                VendorBillId = bill.Id,
                VendorId = bill.VendorId,
                CurrencyId = bill.CurrencyId,
                Amount = pick.Amount,
                DiscountAmount = discount,
                Status = PaymentRunItemStatus.Pending,
            });
        }

        if (request.ApplyVendorCredits)
            await AllocateCreditsAsync(db, run, bills, ct);

        await using (var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            run.RunNumber = await VendorCreditMemoRules.NextRunNumberAsync(db, ct);
            db.PaymentRuns.Add(run);
            await db.SaveChangesAsync(ct);
            if (tx is not null)
                await tx.CommitAsync(ct);
        }

        return run;
    }

    private static async Task AllocateCreditsAsync(AppDbContext db, PaymentRun run, Dictionary<int, VendorBill> bills, CancellationToken ct)
    {
        foreach (var group in run.Items.GroupBy(i => (i.VendorId, i.CurrencyId)))
        {
            var memos = await db.VendorCreditMemos.AsNoTracking()
                .Include(m => m.Lines)
                .Include(m => m.Applications)
                .Where(m => m.VendorId == group.Key.VendorId && m.CurrencyId == group.Key.CurrencyId
                    && (m.Status == VendorCreditMemoStatus.Open || m.Status == VendorCreditMemoStatus.PartiallyApplied))
                .OrderBy(m => m.MemoDate).ThenBy(m => m.Id)
                .ToListAsync(ct);
            var open = memos.Where(m => m.OpenAmount > 0m).Select(m => (Memo: m, Left: m.OpenAmount)).ToList();
            if (open.Count == 0)
                continue;

            var items = group.OrderBy(i => bills[i.VendorBillId].DueDate).ThenBy(i => i.VendorBillId).ToList();
            var taken = Allocate(run, items, open, i => i.Amount);
            if (items.Sum(i => i.Amount) - taken > 0m)
                continue;

            foreach (var item in items)
            {
                foreach (var credit in item.Credits)
                    run.Credits.Remove(credit);
                item.Credits.Clear();
            }
            open = memos.Where(m => m.OpenAmount > 0m).Select(m => (Memo: m, Left: m.OpenAmount)).ToList();
            Allocate(run, items, open, i => bills[i.VendorBillId].BalanceDue);
            foreach (var item in items)
                item.DiscountAmount = 0m;
        }
    }

    private static decimal Allocate(
        PaymentRun run, List<PaymentRunItem> items, List<(VendorCreditMemo Memo, decimal Left)> open,
        Func<PaymentRunItem, decimal> cap)
    {
        var taken = 0m;
        var memoIndex = 0;
        foreach (var item in items)
        {
            var need = cap(item);
            while (need > 0m && memoIndex < open.Count)
            {
                var (memo, left) = open[memoIndex];
                var amount = Math.Min(need, left);
                if (amount > 0m)
                {
                    var credit = new PaymentRunCredit { VendorCreditMemoId = memo.Id, Amount = amount, PaymentRunItem = item };
                    item.Credits.Add(credit);
                    run.Credits.Add(credit);
                    need -= amount;
                    taken += amount;
                    open[memoIndex] = (memo, left - amount);
                }
                if (open[memoIndex].Left <= 0m)
                    memoIndex++;
            }
        }
        return taken;
    }
}
