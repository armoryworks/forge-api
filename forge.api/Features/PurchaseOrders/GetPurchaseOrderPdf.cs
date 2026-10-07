using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

public record GetPurchaseOrderPdfQuery(int Id) : IRequest<byte[]>;

public class GetPurchaseOrderPdfHandler(AppDbContext db) : IRequestHandler<GetPurchaseOrderPdfQuery, byte[]>
{
    private static readonly string[] CompanyKeys = ["company.name", "company_name", "company.phone", "company.email"];

    public async Task<byte[]> Handle(GetPurchaseOrderPdfQuery request, CancellationToken ct)
    {
        var document = await BuildDocumentAsync(request.Id, ct);
        return document.GeneratePdf();
    }

    public async Task<PurchaseOrderPdfDocument> BuildDocumentAsync(int purchaseOrderId, CancellationToken ct)
    {
        var po = await db.PurchaseOrders
            .AsNoTracking()
            .Include(p => p.Vendor)
            .Include(p => p.Lines).ThenInclude(l => l.Part!).ThenInclude(p => p.PurchaseUom)
            .Include(p => p.Lines).ThenInclude(l => l.Part!).ThenInclude(p => p.StockUom)
            .Include(p => p.Lines).ThenInclude(l => l.Uom)
            .Include(p => p.Lines).ThenInclude(l => l.PurchaseUnit)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == purchaseOrderId, ct)
            ?? throw new KeyNotFoundException($"Purchase order {purchaseOrderId} not found");

        var partIds = po.Lines.Where(l => l.PartId.HasValue).Select(l => l.PartId!.Value).Distinct().ToList();
        var vendorParts = await db.VendorParts
            .AsNoTracking()
            .Where(vp => vp.VendorId == po.VendorId && partIds.Contains(vp.PartId) && vp.VendorPartNumber != null)
            .Select(vp => new { vp.PartId, vp.VendorPartNumber })
            .ToListAsync(ct);
        var vendorPartNumbers = vendorParts
            .Where(vp => !string.IsNullOrWhiteSpace(vp.VendorPartNumber))
            .GroupBy(vp => vp.PartId)
            .ToDictionary(g => g.Key, g => g.First().VendorPartNumber!);

        var settings = await db.SystemSettings
            .AsNoTracking()
            .Where(s => CompanyKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var shipTo = await db.CompanyLocations
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Id)
            .FirstOrDefaultAsync(ct);

        return new PurchaseOrderPdfDocument(
            po,
            vendorPartNumbers,
            Setting(settings, "company.name") ?? Setting(settings, "company_name"),
            Setting(settings, "company.phone"),
            Setting(settings, "company.email"),
            shipTo);
    }

    private static string? Setting(Dictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
