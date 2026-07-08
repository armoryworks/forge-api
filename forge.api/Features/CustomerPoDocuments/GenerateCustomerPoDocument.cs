using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerPoDocuments;

public record GenerateCustomerPoDocumentCommand(int SalesOrderId, int? GeneratedFromQuoteId = null)
    : IRequest<CustomerPoDocumentSummaryModel>;

/// <summary>
/// S4a — creates the thin customer-PO identity record for a sales order.
/// Idempotent: if a live (non-deleted) row already exists for the order, it
/// is returned untouched. The document body always renders live from the SO
/// (see GetCustomerPoDocument) — this handler only mints the identity.
///
/// <para><strong>Numbering.</strong> <c>CPO-{seq:D5}</c>, following the
/// existing app-side convention (SalesOrderRepository.GenerateNextOrderNumberAsync):
/// read the latest row's number and increment. A Postgres sequence would be
/// safer, but schema is owned by forge-db and no sequence exists for this
/// table, so the unique index <c>ux_customer_po_documents_document_number</c>
/// is the concurrency backstop — on a unique violation we recompute once and
/// retry. The same retry also resolves a concurrent generate for the SAME
/// order (<c>ux_customer_po_documents_sales_order_id</c>): the re-entry
/// existing-row check returns the winner's row.</para>
/// </summary>
public class GenerateCustomerPoDocumentHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GenerateCustomerPoDocumentCommand, CustomerPoDocumentSummaryModel>
{
    public async Task<CustomerPoDocumentSummaryModel> Handle(
        GenerateCustomerPoDocumentCommand request, CancellationToken ct)
    {
        var order = await db.SalesOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId, ct)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        for (var attempt = 0; ; attempt++)
        {
            // Idempotence: the soft-delete global filter scopes this to the live row.
            var existing = await db.CustomerPoDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.SalesOrderId == request.SalesOrderId, ct);
            if (existing is not null)
                return ToSummary(existing);

            var document = new CustomerPoDocument
            {
                SalesOrderId = order.Id,
                DocumentNumber = await NextDocumentNumberAsync(ct),
                GeneratedFromQuoteId = request.GeneratedFromQuoteId,
                GeneratedAt = clock.UtcNow,
            };
            db.CustomerPoDocuments.Add(document);
            db.LogActivityAt(
                "customer-po-generated",
                $"Customer PO document {document.DocumentNumber} generated for order {order.OrderNumber}",
                ("SalesOrder", order.Id));

            try
            {
                await db.SaveChangesAsync(ct);
                return ToSummary(document);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Unique-index backstop fired (concurrent generate). Detach this
                // attempt's adds and retry once with a fresh number / fresh
                // existing-row check.
                db.Entry(document).State = EntityState.Detached;
                foreach (var entry in db.ChangeTracker.Entries<ActivityLog>()
                             .Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<string> NextDocumentNumberAsync(CancellationToken ct)
    {
        // Mirrors SalesOrderRepository.GenerateNextOrderNumberAsync: latest
        // row (including soft-deleted, so numbers are never reused) + 1.
        var last = await db.CustomerPoDocuments
            .IgnoreQueryFilters()
            .OrderByDescending(d => d.Id)
            .Select(d => d.DocumentNumber)
            .FirstOrDefaultAsync(ct);

        if (last != null && last.StartsWith("CPO-") && int.TryParse(last[4..], out var lastNum))
            return $"CPO-{lastNum + 1:D5}";

        return "CPO-00001";
    }

    private static CustomerPoDocumentSummaryModel ToSummary(CustomerPoDocument d) =>
        new(d.Id, d.SalesOrderId, d.DocumentNumber, d.GeneratedFromQuoteId, d.GeneratedAt);
}
