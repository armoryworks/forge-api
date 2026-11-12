using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record GeneratePickListPdfQuery(int WaveId) : IRequest<byte[]>;

public class GeneratePickListPdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings,
    IClock clock,
    IHtmlPdfRenderer renderer) : IRequestHandler<GeneratePickListPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GeneratePickListPdfQuery request, CancellationToken ct)
    {
        var wave = await db.PickWaves
            .AsNoTracking()
            .Include(w => w.Lines).ThenInclude(l => l.Part)
            .Include(w => w.Lines).ThenInclude(l => l.FromLocation)
            .FirstOrDefaultAsync(w => w.Id == request.WaveId, ct)
            ?? throw new KeyNotFoundException($"Pick wave {request.WaveId} not found");

        var companyName = await CompanyIdentity.GetCompanyNameAsync(settings, ct);

        return await renderer.RenderAsync(PickListPdfDocument.Build(wave, companyName, clock.UtcNow), ct);
    }
}
