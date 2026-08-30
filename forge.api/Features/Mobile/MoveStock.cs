using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record MoveStockCommand(
    int PartId,
    int FromLocationId,
    int ToLocationId,
    decimal Quantity,
    string? LotNumber,
    string DeviceKey) : IRequest<StockMoveResponseModel>;

public class MoveStockValidator : AbstractValidator<MoveStockCommand>
{
    public MoveStockValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.ToLocationId).NotEqual(x => x.FromLocationId)
            .WithMessage("From and to bins must differ.");
    }
}

/// <summary>
/// Scan part → scan from-bin → scan to-bin → quantity. Resolves the source
/// bin content (lot required when the part is lot-tracked) and runs the
/// standard transfer, then hands back the exact reverse move for undo.
/// </summary>
public class MoveStockHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<MoveStockCommand, StockMoveResponseModel>
{
    public async Task<StockMoveResponseModel> Handle(MoveStockCommand request, CancellationToken ct)
    {
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == request.PartId)
            .Select(p => new { p.PartNumber, p.TraceabilityType })
            .FirstOrDefaultAsync(ct) ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        if (part.TraceabilityType != TraceabilityType.None && string.IsNullOrWhiteSpace(request.LotNumber))
            throw new InvalidOperationException("This part is lot-tracked — pick a lot.");

        var candidates = db.BinContents
            .Where(b => b.LocationId == request.FromLocationId
                && b.EntityType == "part" && b.EntityId == request.PartId
                && b.RemovedAt == null && b.Quantity >= request.Quantity);
        if (!string.IsNullOrWhiteSpace(request.LotNumber))
            candidates = candidates.Where(b => b.LotNumber == request.LotNumber);

        var source = await candidates.OrderByDescending(b => b.Quantity).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Not enough of that part in the from-bin.");

        if (request.Quantity != Math.Floor(request.Quantity))
            throw new InvalidOperationException("Move whole units from the phone.");

        await mediator.Send(new TransferStockCommand(new TransferStockRequestModel(
            source.Id, request.ToLocationId, (int)request.Quantity, $"mobile:{request.DeviceKey}")), ct);

        var names = await db.StorageLocations.AsNoTracking()
            .Where(l => l.Id == request.FromLocationId || l.Id == request.ToLocationId)
            .Select(l => new { l.Id, l.Name })
            .ToListAsync(ct);
        string NameOf(int id) => names.FirstOrDefault(n => n.Id == id)?.Name ?? $"#{id}";

        return new StockMoveResponseModel(
            part.PartNumber, NameOf(request.FromLocationId), NameOf(request.ToLocationId),
            request.Quantity, source.LotNumber,
            new StockMoveUndoModel(request.PartId, request.ToLocationId, request.FromLocationId,
                request.Quantity, source.LotNumber));
    }
}
