using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CreditMemos;

public record VoidCustomerRefundCommand(int RefundId, string Reason) : IRequest;

public class VoidCustomerRefundValidator : AbstractValidator<VoidCustomerRefundCommand>
{
    public VoidCustomerRefundValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>Voids a recorded refund (a cheque that never cleared, a refund keyed twice): the credit or cash it paid back is open again.</summary>
public class VoidCustomerRefundHandler(AppDbContext db, ICreditMemoPostingService posting, IClock clock)
    : IRequestHandler<VoidCustomerRefundCommand>
{
    public async Task Handle(VoidCustomerRefundCommand request, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var refund = await db.CustomerRefunds
            .Include(r => r.CreditMemo).ThenInclude(m => m!.Refunds)
            .Include(r => r.CreditMemo).ThenInclude(m => m!.Applications)
            .Include(r => r.CreditMemo).ThenInclude(m => m!.Lines)
            .FirstOrDefaultAsync(r => r.Id == request.RefundId, ct)
            ?? throw new KeyNotFoundException($"Refund {request.RefundId} not found");
        if (refund.Status == CustomerRefundStatus.Voided)
            throw new InvalidOperationException($"Refund {refund.RefundNumber} is already voided.");

        var now = clock.UtcNow;
        refund.Status = CustomerRefundStatus.Voided;
        refund.VoidedAt = now;
        refund.VoidReason = request.Reason.Trim();
        refund.CreditMemo?.RecomputeStatus();

        db.LogActivityAt("refund-voided", $"Voided refund {refund.RefundNumber}: {refund.VoidReason}",
            refund.CreditMemoId is int memoId ? ("CreditMemo", memoId) : ("Payment", refund.PaymentId ?? 0),
            ("Customer", refund.CustomerId));
        await db.SaveChangesAsync(ct);

        await posting.ReverseRefundAsync(refund.Id, await CreditMemoRules.ShopTodayAsync(db, now, ct), CreditMemoRules.UserId(db), ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
    }
}
