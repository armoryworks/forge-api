using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record GetKioskTerminalsQuery : IRequest<List<KioskTerminalModel>>;

public class GetKioskTerminalsHandler(AppDbContext db) : IRequestHandler<GetKioskTerminalsQuery, List<KioskTerminalModel>>
{
    public async Task<List<KioskTerminalModel>> Handle(GetKioskTerminalsQuery request, CancellationToken ct)
    {
        return await db.KioskTerminals
            .Include(k => k.Team)
            .Where(k => k.IsActive)
            .OrderBy(k => k.Name)
            .Select(k => new KioskTerminalModel(k.Id, k.Name, k.DeviceToken, k.TeamId, k.Team.Name, k.Team.Color))
            .ToListAsync(ct);
    }
}
