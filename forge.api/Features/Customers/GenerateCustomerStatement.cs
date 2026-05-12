using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Data.Context;

namespace Forge.Api.Features.Customers;

public record GenerateCustomerStatementQuery(int CustomerId) : IRequest<byte[]>;

public class GenerateCustomerStatementHandler(AppDbContext db)
    : IRequestHandler<GenerateCustomerStatementQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateCustomerStatementQuery request, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([request.CustomerId], ct)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        var invoices = await db.Invoices
            .Include(i => i.Lines)
            .Include(i => i.PaymentApplications)
            .Where(i => i.CustomerId == request.CustomerId
                && i.Status != Forge.Core.Enums.InvoiceStatus.Voided
                && i.Status != Forge.Core.Enums.InvoiceStatus.Draft)
            .ToListAsync(ct);

        var payments = await db.Payments
            .Where(p => p.CustomerId == request.CustomerId)
            .ToListAsync(ct);

        var companySetting = await db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == "company_name", ct);
        var companyName = companySetting?.Value ?? "QB Engineer";

        var document = new CustomerStatementPdfDocument(
            customer, invoices, payments, companyName, DateTimeOffset.UtcNow);

        return document.GeneratePdf();
    }
}
