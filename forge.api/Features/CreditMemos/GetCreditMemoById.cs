using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CreditMemos;

public record GetCreditMemoByIdQuery(int Id) : IRequest<CreditMemoDetailResponseModel>;

public class GetCreditMemoByIdHandler(AppDbContext db) : IRequestHandler<GetCreditMemoByIdQuery, CreditMemoDetailResponseModel>
{
    public async Task<CreditMemoDetailResponseModel> Handle(GetCreditMemoByIdQuery request, CancellationToken ct)
    {
        var memo = await db.CreditMemos.AsNoTracking()
            .Include(m => m.Customer)
            .Include(m => m.Currency)
            .Include(m => m.Invoice)
            .Include(m => m.CustomerReturn)
            .Include(m => m.Lines).ThenInclude(l => l.Part)
            .Include(m => m.Applications).ThenInclude(a => a.Invoice)
            .Include(m => m.Refunds)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Credit memo {request.Id} not found");

        var issueKey = CreditMemoPostingService.IssueKey(memo.Id);
        var journalId = await db.JournalEntries.AsNoTracking()
            .Where(e => e.IdempotencyKey == issueKey)
            .Select(e => (long?)e.Id)
            .FirstOrDefaultAsync(ct);

        return new CreditMemoDetailResponseModel(
            memo.Id, memo.CreditMemoNumber, memo.CustomerId, memo.Customer.Name,
            memo.InvoiceId, memo.Invoice?.InvoiceNumber, memo.CustomerReturnId, memo.CustomerReturn?.ReturnNumber,
            memo.ReasonType.ToString(), memo.Reason, memo.Status.ToString(), memo.CreditDate, memo.TaxRate,
            memo.Subtotal, memo.TaxAmount, memo.Total, memo.AmountApplied, memo.AmountRefunded, memo.OpenAmount,
            memo.CurrencyId, memo.Currency?.Code, memo.FxRate, memo.Notes, memo.IssuedAt, memo.VoidedAt, memo.VoidReason,
            journalId,
            memo.Lines.OrderBy(l => l.LineNumber)
                .Select(l => new CreditMemoLineModel(
                    l.Id, l.LineNumber, l.LineType.ToString(), l.PartId, l.Part?.PartNumber, l.InvoiceLineId,
                    l.Description, l.Quantity, l.UnitPrice, l.IsTaxable, l.SignedTotal))
                .ToList(),
            memo.Applications.OrderBy(a => a.AppliedAt)
                .Select(a => new CreditMemoApplicationModel(
                    a.Id, memo.Id, memo.CreditMemoNumber, a.InvoiceId, a.Invoice?.InvoiceNumber, a.Amount, a.AppliedAt, a.ReversedAt))
                .ToList(),
            memo.Refunds.OrderBy(r => r.RefundDate)
                .Select(r => new CustomerRefundModel(
                    r.Id, r.RefundNumber, r.CustomerId, r.Amount, r.Method.ToString(), r.RefundDate, r.Reference,
                    r.Status.ToString(), r.CreditMemoId, memo.CreditMemoNumber, r.PaymentId, null, memo.Currency?.Code, r.FxRate))
                .ToList(),
            memo.CreatedAt, memo.UpdatedAt, memo.Version);
    }
}
