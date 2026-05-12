using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record GeneratePackingSlipPdfQuery(int Id) : IRequest<byte[]>;

public class GeneratePackingSlipPdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings) : IRequestHandler<GeneratePackingSlipPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GeneratePackingSlipPdfQuery request, CancellationToken ct)
    {
        var shipment = await db.Shipments
            .Include(s => s.SalesOrder)
                .ThenInclude(so => so.Customer)
            .Include(s => s.ShippingAddress)
            .Include(s => s.Lines)
                .ThenInclude(l => l.SalesOrderLine!)
                    .ThenInclude(sol => sol.Part)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        var companySetting = await settings.FindByKeyAsync("company_name", ct);
        var companyName = companySetting?.Value ?? "QB Engineer";

        var document = new PackingSlipPdfDocument(shipment, companyName);
        return document.GeneratePdf();
    }
}
