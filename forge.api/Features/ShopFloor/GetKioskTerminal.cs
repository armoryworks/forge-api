using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record GetKioskTerminalQuery(string DeviceToken) : IRequest<KioskTerminalModel?>;

public class GetKioskTerminalHandler(AppDbContext db) : IRequestHandler<GetKioskTerminalQuery, KioskTerminalModel?>
{
    public async Task<KioskTerminalModel?> Handle(GetKioskTerminalQuery request, CancellationToken ct)
    {
        var terminal = await db.KioskTerminals
            .Include(t => t.Team)
            .FirstOrDefaultAsync(t => t.DeviceToken == request.DeviceToken && t.IsActive, ct);

        if (terminal == null) return null;

        return new KioskTerminalModel(
            terminal.Id,
            terminal.Name,
            terminal.DeviceToken,
            terminal.Team.Id,
            terminal.Team.Name,
            terminal.Team.Color);
    }
}
