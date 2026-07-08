using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPoDocuments;

public record GetCustomerPoDocumentPdfQuery(int SalesOrderId) : IRequest<byte[]>;

/// <summary>
/// S4a — renders the internal customer-PO document as a PDF. Mirrors
/// Features/Invoices/GenerateInvoicePdf.cs; the PDF body is composed from
/// the CURRENT sales order state at request time (live view, not a snapshot).
/// </summary>
public class GetCustomerPoDocumentPdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings) : IRequestHandler<GetCustomerPoDocumentPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetCustomerPoDocumentPdfQuery request, CancellationToken ct)
    {
        var document = await db.CustomerPoDocuments
            .AsNoTracking()
            .Include(d => d.GeneratedFromQuote)
            .Include(d => d.SalesOrder).ThenInclude(so => so.Customer)
            .Include(d => d.SalesOrder).ThenInclude(so => so.Lines).ThenInclude(l => l.Part)
            .Include(d => d.SalesOrder).ThenInclude(so => so.ShippingAddress)
            .FirstOrDefaultAsync(d => d.SalesOrderId == request.SalesOrderId, ct)
            ?? throw new KeyNotFoundException(
                $"No customer PO document exists for sales order {request.SalesOrderId}");

        var companySetting = await settings.FindByKeyAsync("company_name", ct);
        var companyName = companySetting?.Value ?? "QB Engineer";

        var pdf = new CustomerPoPdfDocument(document, companyName);
        return pdf.GeneratePdf();
    }
}
