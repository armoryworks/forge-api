using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record GenerateBillOfLadingPdfQuery(int Id) : IRequest<byte[]>;

public class GenerateBillOfLadingPdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings,
    IHtmlPdfRenderer renderer) : IRequestHandler<GenerateBillOfLadingPdfQuery, byte[]>
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
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        var companyName = await CompanyIdentity.GetCompanyNameAsync(settings, ct);
        var companyAddress = (await settings.FindByKeyAsync("company_address", ct))?.Value;
        var companyPhone = (await settings.FindByKeyAsync("company_phone", ct))?.Value;

        var customs = await ShipmentCustoms.LoadAsync(db, shipment, ct);

        return await renderer.RenderAsync(
            BillOfLadingPdfDocument.Build(shipment, companyName, companyAddress, companyPhone, customs), ct);
    }
}
