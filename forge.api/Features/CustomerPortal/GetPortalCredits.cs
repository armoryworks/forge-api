using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// The customer's issued credit memos and recorded refunds, newest first. ⚡ Standalone Financial: empty, flagged
/// <see cref="PortalList{T}.ExternalAccounting"/>, when an external accounting provider is connected.
/// </summary>
public record GetPortalCreditsQuery(int CustomerId) : IRequest<PortalList<PortalCreditListItem>>;

public class GetPortalCreditsHandler(AppDbContext db, IAccountingService accounting)
    : IRequestHandler<GetPortalCreditsQuery, PortalList<PortalCreditListItem>>
{
    public async Task<PortalList<PortalCreditListItem>> Handle(GetPortalCreditsQuery request, CancellationToken ct)
    {
        var standalone = string.Equals(accounting.ProviderId, "mock", StringComparison.OrdinalIgnoreCase);
        if (!standalone) return new([], 0, ExternalAccounting: true);

        var memos = await db.CreditMemos.AsNoTracking()
            .Include(m => m.Currency)
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .Include(m => m.Refunds)
            .Where(m => m.CustomerId == request.CustomerId
                && m.Status != CreditMemoStatus.Draft && m.Status != CreditMemoStatus.Voided)
            .OrderByDescending(m => m.CreditDate)
            .Take(PortalReadRules.ListCap)
            .AsSplitQuery()
            .ToListAsync(ct);
        var refunds = await db.CustomerRefunds.AsNoTracking()
            .Include(r => r.Currency)
            .Where(r => r.CustomerId == request.CustomerId && r.Status == CustomerRefundStatus.Recorded)
            .OrderByDescending(r => r.RefundDate)
            .Take(PortalReadRules.ListCap)
            .ToListAsync(ct);

        var items = memos
            .Select(m => new PortalCreditListItem(
                "CreditMemo", m.Id, m.CreditMemoNumber, m.CreditDate, m.Reason, m.Total, m.OpenAmount,
                m.Status.ToString(), m.Currency?.Code))
            .Concat(refunds.Select(r => new PortalCreditListItem(
                "Refund", r.Id, r.RefundNumber, r.RefundDate, r.Method.ToString(), r.Amount, 0m,
                r.Status.ToString(), r.Currency?.Code)))
            .OrderByDescending(i => i.Date)
            .Take(PortalReadRules.ListCap)
            .ToList();
        return new(items, items.Count);
    }
}
