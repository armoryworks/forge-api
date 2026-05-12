using MediatR;

using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record GeneratePickListPdfQuery(int WaveId) : IRequest<byte[]>;

public class GeneratePickListPdfHandler(
    AppDbContext db,
    ISystemSettingRepository settings,
    IClock clock) : IRequestHandler<GeneratePickListPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GeneratePickListPdfQuery request, CancellationToken ct)
    {
        var wave = await db.PickWaves
            .AsNoTracking()
            .Include(w => w.Lines).ThenInclude(l => l.Part)
            .Include(w => w.Lines).ThenInclude(l => l.FromLocation)
            .FirstOrDefaultAsync(w => w.Id == request.WaveId, ct)
            ?? throw new KeyNotFoundException($"Pick wave {request.WaveId} not found");

        var companySetting = await settings.FindByKeyAsync("company_name", ct);
        var companyName = companySetting?.Value ?? "QB Engineer";

        var document = new PickListPdfDocument(wave, companyName, clock.UtcNow);
        return document.GeneratePdf();
    }
}
