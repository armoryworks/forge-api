using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record DeleteKioskTerminalCommand(int Id) : IRequest;

public class DeleteKioskTerminalHandler(AppDbContext db) : IRequestHandler<DeleteKioskTerminalCommand>
{
    public async Task Handle(DeleteKioskTerminalCommand request, CancellationToken ct)
    {
        var terminal = await db.KioskTerminals.FirstOrDefaultAsync(t => t.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Kiosk terminal {request.Id} not found");

        // Soft-disable: SetupKioskTerminal re-activates on re-pair via same DeviceToken,
        // so pairing history is preserved.
        terminal.IsActive = false;
        await db.SaveChangesAsync(ct);
    }
}
