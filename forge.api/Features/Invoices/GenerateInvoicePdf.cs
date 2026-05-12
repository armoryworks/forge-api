using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Invoices;

public record GenerateInvoicePdfQuery(int Id) : IRequest<byte[]>;

public class GenerateInvoicePdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings) : IRequestHandler<GenerateInvoicePdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateInvoicePdfQuery request, CancellationToken ct)
    {
        var invoice = await db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.SalesOrder)
            .Include(i => i.Lines)
                .ThenInclude(l => l.Part)
            .Include(i => i.PaymentApplications)
            .FirstOrDefaultAsync(i => i.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Invoice {request.Id} not found");

        var companySetting = await settings.FindByKeyAsync("company_name", ct);
        var companyName = companySetting?.Value ?? "QB Engineer";

        var document = new InvoicePdfDocument(invoice, companyName);
        return document.GeneratePdf();
    }
}
