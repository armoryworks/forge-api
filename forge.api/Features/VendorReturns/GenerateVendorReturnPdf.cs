using MediatR;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

using Forge.Api.Features.VendorCredits;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorReturns;

public record GenerateVendorReturnPdfQuery(int Id, string? Language = null) : IRequest<byte[]>;

public class GenerateVendorReturnPdfHandler(AppDbContext db, ISystemSettingRepository settings)
    : IRequestHandler<GenerateVendorReturnPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GenerateVendorReturnPdfQuery request, CancellationToken ct)
    {
        var rtv = await db.VendorReturns.AsNoTracking()
            .Include(r => r.Vendor)
            .Include(r => r.NonConformance)
            .Include(r => r.PurchaseOrder)
            .Include(r => r.Lines).ThenInclude(l => l.Part)
            .Include(r => r.Lines).ThenInclude(l => l.Location)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Vendor return {request.Id} not found");

        var companyName = await CompanyIdentity.GetCompanyNameAsync(settings, ct);
        var money = await MoneyFormat.ForCurrencyIdAsync(db, rtv.CurrencyId, await MoneyFormat.BaseCodeAsync(settings, ct), ct);
        return new VendorReturnPdfDocument(rtv, companyName, money, VendorDocumentPdfLabels.For(request.Language)).GeneratePdf();
    }
}
