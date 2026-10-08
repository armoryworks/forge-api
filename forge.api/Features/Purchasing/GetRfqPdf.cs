using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Data.Context;

namespace Forge.Api.Features.Purchasing;

public record GetRfqPdfQuery(int Id, int? VendorId = null) : IRequest<byte[]>;

public class GetRfqPdfHandler(AppDbContext db) : IRequestHandler<GetRfqPdfQuery, byte[]>
{
    private static readonly string[] CompanyKeys = ["company.name", "company_name", "company.phone", "company.email"];

    public async Task<byte[]> Handle(GetRfqPdfQuery request, CancellationToken ct)
    {
        var document = await BuildDocumentAsync(request.Id, request.VendorId, ct);
        return document.GeneratePdf();
    }

    public async Task<RfqPdfDocument> BuildDocumentAsync(int rfqId, int? vendorId, CancellationToken ct)
    {
        var rfq = await db.RequestForQuotes
            .AsNoTracking()
            .Include(r => r.Part).ThenInclude(p => p.StockUom)
            .FirstOrDefaultAsync(r => r.Id == rfqId, ct)
            ?? throw new KeyNotFoundException($"RFQ {rfqId} not found");

        var vendor = vendorId is int id
            ? await db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct)
                ?? throw new KeyNotFoundException($"Vendor {id} not found")
            : null;

        var settings = await db.SystemSettings
            .AsNoTracking()
            .Where(s => CompanyKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var location = await db.CompanyLocations
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Id)
            .FirstOrDefaultAsync(ct);

        return new RfqPdfDocument(
            rfq,
            vendor,
            Setting(settings, "company.name") ?? Setting(settings, "company_name"),
            Setting(settings, "company.phone"),
            Setting(settings, "company.email"),
            location);
    }

    private static string? Setting(Dictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
