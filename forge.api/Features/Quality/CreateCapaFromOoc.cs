using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateCapaFromOocCommand(int OocEventId) : IRequest<SpcOocEventResponseModel>;

public class CreateCapaFromOocHandler(AppDbContext db)
    : IRequestHandler<CreateCapaFromOocCommand, SpcOocEventResponseModel>
{
    public async Task<SpcOocEventResponseModel> Handle(
        CreateCapaFromOocCommand request, CancellationToken cancellationToken)
    {
        var oocEvent = await db.SpcOocEvents
            .Include(e => e.Characteristic)
            .ThenInclude(c => c.Part)
            .FirstOrDefaultAsync(e => e.Id == request.OocEventId, cancellationToken)
            ?? throw new KeyNotFoundException($"OOC Event {request.OocEventId} not found.");

        if (oocEvent.Status == SpcOocStatus.Resolved)
            throw new InvalidOperationException($"OOC Event {request.OocEventId} is already resolved.");

        // CAPA system (#6) not yet implemented — mark status as CapaCreated
        // When CAPA entities exist, this will create a CAPA record and set CapaId
        oocEvent.Status = SpcOocStatus.CapaCreated;
        await db.SaveChangesAsync(cancellationToken);

        string? acknowledgedByName = null;
        if (oocEvent.AcknowledgedById.HasValue)
        {
            acknowledgedByName = await db.Users.AsNoTracking()
                .Where(u => u.Id == oocEvent.AcknowledgedById.Value)
                .Select(u => u.LastName + ", " + u.FirstName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new SpcOocEventResponseModel
        {
            Id = oocEvent.Id,
            CharacteristicId = oocEvent.CharacteristicId,
            CharacteristicName = oocEvent.Characteristic.Name,
            PartNumber = oocEvent.Characteristic.Part.PartNumber,
            MeasurementId = oocEvent.MeasurementId,
            DetectedAt = oocEvent.DetectedAt,
            RuleName = oocEvent.RuleName,
            Description = oocEvent.Description,
            Severity = oocEvent.Severity.ToString(),
            Status = oocEvent.Status.ToString(),
            AcknowledgedByName = acknowledgedByName,
            AcknowledgedAt = oocEvent.AcknowledgedAt,
            AcknowledgmentNotes = oocEvent.AcknowledgmentNotes,
            CapaId = oocEvent.CapaId,
        };
    }
}
