using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Inventory;

public record ReleaseReservationCommand(int Id) : IRequest;

public class ReleaseReservationHandler(IInventoryRepository repo) : IRequestHandler<ReleaseReservationCommand>
{
    public async Task Handle(ReleaseReservationCommand request, CancellationToken ct)
    {
        var reservation = await repo.FindReservationAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Reservation {request.Id} not found.");

        var binContent = reservation.BinContent;

        reservation.DeletedAt = DateTimeOffset.UtcNow;

        binContent.ReservedQuantity = Math.Max(0, binContent.ReservedQuantity - reservation.Quantity);

        await repo.SaveChangesAsync(ct);
    }
}
