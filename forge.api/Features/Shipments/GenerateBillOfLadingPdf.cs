using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record GenerateBillOfLadingPdfQuery(int Id) : IRequest<byte[]>;

public class GenerateBillOfLadingPdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings) : IRequestHandler<GenerateBillOfLadingPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateBillOfLadingPdfQuery request, CancellationToken ct)
    {
        var shipment = await db.Shipments
            .AsNoTracking()
            .Include(s => s.SalesOrder)
                .ThenInclude(so => so.Customer)
            .Include(s => s.ShippingAddress)
            .Include(s => s.Lines)
                .ThenInclude(l => l.SalesOrderLine!)
                    .ThenInclude(sol => sol.Part)
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        var companyName = (await settings.FindByKeyAsync("company_name", ct))?.Value ?? "QB Engineer";
        var companyAddress = (await settings.FindByKeyAsync("company_address", ct))?.Value;
        var companyPhone = (await settings.FindByKeyAsync("company_phone", ct))?.Value;

        var document = new BillOfLadingPdfDocument(shipment, companyName, companyAddress, companyPhone);
        return document.GeneratePdf();
    }
}
