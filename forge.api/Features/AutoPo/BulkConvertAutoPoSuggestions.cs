using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.AutoPo;

public record BulkConvertAutoPoSuggestionsCommand(List<int> SuggestionIds, int UserId) : IRequest<List<int>>;

public class BulkConvertAutoPoSuggestionsHandler(
    AppDbContext db,
    IPurchaseOrderRepository poRepo,
    IBarcodeService barcodeService) : IRequestHandler<BulkConvertAutoPoSuggestionsCommand, List<int>>
{
    public async Task<List<int>> Handle(BulkConvertAutoPoSuggestionsCommand request, CancellationToken ct)
    {
        var suggestions = await db.AutoPoSuggestions
            .Include(s => s.Part)
            .Include(s => s.Vendor)
            .Where(s => request.SuggestionIds.Contains(s.Id) && s.Status == AutoPoSuggestionStatus.Pending)
            .ToListAsync(ct);

        if (suggestions.Count == 0)
            throw new KeyNotFoundException("No pending suggestions found for the given IDs");

        // Group by vendor to consolidate into single POs per vendor
        var byVendor = suggestions.GroupBy(s => s.VendorId);
        var createdPoIds = new List<int>();

        foreach (var group in byVendor)
        {
            var vendorSuggestions = group.ToList();
            var poNumber = await poRepo.GenerateNextPONumberAsync(ct);

            var earliestNeededBy = vendorSuggestions.Min(s => s.NeededByDate);
            var suggestionIds = string.Join(", ", vendorSuggestions.Select(s => $"#{s.Id}"));

            // S4b provenance — MRP-suggested PO. OriginReference is
            // varchar(200); truncate defensively for large batches.
            var originReference = $"Auto-PO suggestions {suggestionIds}";
            if (originReference.Length > 200)
                originReference = originReference[..200];

            var po = new PurchaseOrder
            {
                PONumber = poNumber,
                VendorId = group.Key,
                Status = PurchaseOrderStatus.Draft,
                ExpectedDeliveryDate = earliestNeededBy,
                Notes = $"Auto-generated from demand suggestions: {suggestionIds}",
                OriginSource = PoOriginSource.AutoMrp,
                OriginUserId = request.UserId > 0 ? request.UserId : null,
                OriginReference = originReference,
            };

            foreach (var suggestion in vendorSuggestions)
            {
                po.Lines.Add(new PurchaseOrderLine
                {
                    PartId = suggestion.PartId,
                    Description = suggestion.Part.Description ?? suggestion.Part.Name,
                    OrderedQuantity = suggestion.SuggestedQty,
                    UnitPrice = 0,
                });
            }

            await poRepo.AddAsync(po, ct);
            await db.SaveChangesAsync(ct);

            await barcodeService.CreateBarcodeAsync(
                BarcodeEntityType.PurchaseOrder, po.Id, po.PONumber, ct);

            createdPoIds.Add(po.Id);

            foreach (var suggestion in vendorSuggestions)
            {
                suggestion.Status = AutoPoSuggestionStatus.Converted;
                suggestion.ConvertedPurchaseOrderId = po.Id;
            }
        }

        await db.SaveChangesAsync(ct);
        return createdPoIds;
    }
}
