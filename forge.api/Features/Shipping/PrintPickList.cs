using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record PrintPickListQuery(int WaveId) : IRequest<PickWaveResponseModel>;

public class PrintPickListHandler(AppDbContext db) : IRequestHandler<PrintPickListQuery, PickWaveResponseModel>
{
    public async Task<PickWaveResponseModel> Handle(PrintPickListQuery request, CancellationToken cancellationToken)
    {
        // Returns the wave data for PDF generation — actual PDF rendering handled at controller level
        var wave = await db.PickWaves
            .AsNoTracking()
            .Include(w => w.Lines).ThenInclude(l => l.Part)
            .Include(w => w.Lines).ThenInclude(l => l.FromLocation)
            .Where(w => w.Id == request.WaveId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Pick wave {request.WaveId} not found");

        return new PickWaveResponseModel
        {
            Id = wave.Id,
            WaveNumber = wave.WaveNumber,
            Status = wave.Status,
            Strategy = wave.Strategy,
            AssignedToId = wave.AssignedToId,
            TotalLines = wave.TotalLines,
            PickedLines = wave.PickedLines,
            ReleasedAt = wave.ReleasedAt,
            StartedAt = wave.StartedAt,
            CompletedAt = wave.CompletedAt,
            Notes = wave.Notes,
            CreatedAt = wave.CreatedAt,
            Lines = wave.Lines.OrderBy(l => l.SortOrder).Select(l => new PickLineResponseModel
            {
                Id = l.Id,
                ShipmentLineId = l.ShipmentLineId,
                PartId = l.PartId,
                PartNumber = l.Part.PartNumber,
                PartDescription = l.Part.Description ?? l.Part.Name,
                FromLocationName = l.FromLocation.Name,
                BinPath = l.BinPath,
                RequestedQuantity = l.RequestedQuantity,
                PickedQuantity = l.PickedQuantity,
                Status = l.Status,
                SortOrder = l.SortOrder,
                PickedAt = l.PickedAt,
                ShortNotes = l.ShortNotes,
            }).ToList(),
        };
    }
}
