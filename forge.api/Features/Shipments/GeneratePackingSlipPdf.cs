using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record GeneratePackingSlipPdfQuery(int Id) : IRequest<byte[]>;

public class GeneratePackingSlipPdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings,
    IHtmlPdfRenderer renderer) : IRequestHandler<GeneratePackingSlipPdfQuery, byte[]>
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
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        var companyName = await CompanyIdentity.GetCompanyNameAsync(settings, ct);

        var customs = await ShipmentCustoms.LoadAsync(db, shipment, ct);

        return await renderer.RenderAsync(PackingSlipPdfDocument.Build(shipment, companyName, customs), ct);
    }
}
