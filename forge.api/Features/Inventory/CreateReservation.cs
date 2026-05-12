using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record CreateReservationCommand(CreateReservationRequestModel Data) : IRequest<ReservationResponseModel>;

public class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        RuleFor(x => x.Data.BinContentId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.Notes).MaximumLength(500).When(x => x.Data.Notes is not null);
    }
}

public class CreateReservationHandler(
    IInventoryRepository repo,
    AppDbContext db)
    : IRequestHandler<CreateReservationCommand, ReservationResponseModel>
{
    public async Task<ReservationResponseModel> Handle(CreateReservationCommand request, CancellationToken ct)
    {
        var data = request.Data;

        var binContent = await repo.FindBinContentWithLocationAsync(data.BinContentId, ct)
            ?? throw new KeyNotFoundException($"Bin content {data.BinContentId} not found.");

        var available = binContent.Quantity - binContent.ReservedQuantity;
        if (data.Quantity > available)
            throw new InvalidOperationException(
                $"Cannot reserve {data.Quantity} — only {available} available in this bin (on hand: {binContent.Quantity}, already reserved: {binContent.ReservedQuantity}).");

        var part = await db.Parts.FirstOrDefaultAsync(p => p.Id == data.PartId, ct)
            ?? throw new KeyNotFoundException($"Part {data.PartId} not found.");

        var reservation = new Reservation
        {
            PartId = data.PartId,
            BinContentId = data.BinContentId,
            JobId = data.JobId,
            SalesOrderLineId = data.SalesOrderLineId,
            Quantity = data.Quantity,
            Notes = data.Notes,
        };

        await repo.AddReservationAsync(reservation, ct);
        binContent.ReservedQuantity += data.Quantity;
        await repo.SaveChangesAsync(ct);

        // Build location path
        var allLocations = await db.StorageLocations
            .Where(l => l.DeletedAt == null)
            .ToListAsync(ct);
        var locById = allLocations.ToDictionary(l => l.Id);

        string BuildPath(StorageLocation loc)
        {
            var parts = new List<string> { loc.Name };
            var current = loc;
            while (current.ParentId.HasValue && locById.TryGetValue(current.ParentId.Value, out var parent))
            {
                parts.Insert(0, parent.Name);
                current = parent;
            }
            return string.Join(" / ", parts);
        }

        Job? job = null;
        if (data.JobId.HasValue)
            job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == data.JobId.Value, ct);

        return new ReservationResponseModel(
            reservation.Id,
            part.Id,
            part.PartNumber,
            part.Description ?? part.Name,
            binContent.Id,
            BuildPath(binContent.Location),
            job?.Id,
            job?.Title,
            job?.JobNumber,
            data.SalesOrderLineId,
            reservation.Quantity,
            reservation.Notes,
            reservation.CreatedAt);
    }
}
