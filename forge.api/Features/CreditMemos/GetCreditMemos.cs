using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CreditMemos;

/// <summary>Credit memos, newest first. <c>OpenOnly</c> keeps issued memos with something left to apply or refund.</summary>
public record GetCreditMemosQuery(
    int? CustomerId = null,
    CreditMemoStatus? Status = null,
    int? InvoiceId = null,
    bool OpenOnly = false,
    string? Search = null) : IRequest<List<CreditMemoListItemModel>>;

public class GetCreditMemosHandler(AppDbContext db) : IRequestHandler<GetCreditMemosQuery, List<CreditMemoListItemModel>>
{
    private const int MaxRows = 500;

    public async Task<List<CreditMemoListItemModel>> Handle(GetCreditMemosQuery request, CancellationToken ct)
    {
        var query = db.CreditMemos.AsNoTracking()
            .Include(m => m.Customer)
            .Include(m => m.Currency)
            .Include(m => m.Invoice)
            .Include(m => m.Lines)
            .Include(m => m.Applications)
            .Include(m => m.Refunds)
            .AsSplitQuery()
            .AsQueryable();

        if (request.CustomerId is int customerId)
            query = query.Where(m => m.CustomerId == customerId);
        if (request.Status is CreditMemoStatus status)
            query = query.Where(m => m.Status == status);
        if (request.InvoiceId is int invoiceId)
            query = query.Where(m => m.InvoiceId == invoiceId || m.Applications.Any(a => a.InvoiceId == invoiceId && a.ReversedAt == null));
        if (request.OpenOnly)
            query = query.Where(m => m.Status == CreditMemoStatus.Issued || m.Status == CreditMemoStatus.PartiallyApplied);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(m => m.CreditMemoNumber.ToLower().Contains(term)
                || m.Customer.Name.ToLower().Contains(term)
                || m.Reason.ToLower().Contains(term));
        }

        var memos = await query
            .OrderByDescending(m => m.CreditDate).ThenByDescending(m => m.Id)
            .Take(MaxRows)
            .ToListAsync(ct);

        return memos
            .Where(m => !request.OpenOnly || m.OpenAmount > 0m)
            .Select(m => new CreditMemoListItemModel(
                m.Id, m.CreditMemoNumber, m.CustomerId, m.Customer.Name, m.InvoiceId, m.Invoice?.InvoiceNumber,
                m.ReasonType.ToString(), m.Reason, m.Status.ToString(), m.CreditDate, m.Total, m.OpenAmount,
                m.CurrencyId, m.Currency?.Code, m.FxRate, m.CreatedAt))
            .ToList();
    }
}
