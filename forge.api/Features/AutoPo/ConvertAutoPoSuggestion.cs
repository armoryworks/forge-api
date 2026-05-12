using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.AutoPo;

public record ConvertAutoPoSuggestionCommand(int SuggestionId, int UserId) : IRequest<int>;

public class ConvertAutoPoSuggestionHandler(
    AppDbContext db,
    IPurchaseOrderRepository poRepo,
    IBarcodeService barcodeService) : IRequestHandler<ConvertAutoPoSuggestionCommand, int>
{
    public async Task<int> Handle(ConvertAutoPoSuggestionCommand request, CancellationToken ct)
    {
        var suggestion = await db.AutoPoSuggestions
            .Include(s => s.Part)
            .Include(s => s.Vendor)
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId, ct)
            ?? throw new KeyNotFoundException($"Auto-PO suggestion {request.SuggestionId} not found");

        if (suggestion.Status != AutoPoSuggestionStatus.Pending)
            throw new InvalidOperationException($"Suggestion {request.SuggestionId} is already {suggestion.Status}");

        var poNumber = await poRepo.GenerateNextPONumberAsync(ct);

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            VendorId = suggestion.VendorId,
            Status = PurchaseOrderStatus.Draft,
            ExpectedDeliveryDate = suggestion.NeededByDate,
            Notes = $"Auto-generated from demand suggestion #{suggestion.Id}",
        };

        po.Lines.Add(new PurchaseOrderLine
        {
            PartId = suggestion.PartId,
            Description = suggestion.Part.Description ?? suggestion.Part.Name,
            OrderedQuantity = suggestion.SuggestedQty,
            UnitPrice = 0, // To be filled by purchasing
        });

        await poRepo.AddAsync(po, ct);

        suggestion.Status = AutoPoSuggestionStatus.Converted;
        suggestion.ConvertedPurchaseOrderId = po.Id;

        await db.SaveChangesAsync(ct);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.PurchaseOrder, po.Id, po.PONumber, ct);

        return po.Id;
    }
}
