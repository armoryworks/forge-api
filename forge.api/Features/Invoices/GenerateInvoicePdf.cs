using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record GenerateInvoicePdfQuery(int Id) : IRequest<byte[]>;

public class GenerateInvoicePdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings,
    IClock? clock = null) : IRequestHandler<GenerateInvoicePdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateInvoicePdfQuery request, CancellationToken ct)
    {
        var invoice = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.SalesOrder)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Part)
            .Include(i => i.PaymentApplications)
            .Include(i => i.CreditApplications)
            .FirstOrDefaultAsync(i => i.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        var companyName = await CompanyIdentity.GetCompanyNameAsync(settings, ct);

        var money = await MoneyFormat.ForCurrencyIdAsync(
            db, invoice.CurrencyId, await MoneyFormat.BaseCodeAsync(settings, ct), ct);
        var offer = await InvoiceDiscountOffer.ForAsync(db, invoice, clock?.UtcNow, ct);
        var document = new InvoicePdfDocument(invoice, companyName, money, offer);
        return document.GeneratePdf();
    }
}
