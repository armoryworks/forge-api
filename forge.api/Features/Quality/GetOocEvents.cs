using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetOocEventsQuery(
    SpcOocStatus? Status,
    SpcOocSeverity? Severity,
    int? CharacteristicId) : IRequest<List<SpcOocEventResponseModel>>;

public class GetOocEventsHandler(AppDbContext db)
    : IRequestHandler<GetOocEventsQuery, List<SpcOocEventResponseModel>>
{
    public async Task<List<SpcOocEventResponseModel>> Handle(
        GetOocEventsQuery request, CancellationToken cancellationToken)
    {
        var query = db.SpcOocEvents.AsNoTracking()
            .Include(e => e.Characteristic)
            .ThenInclude(c => c.Part)
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(e => e.Status == request.Status.Value);

        if (request.Severity.HasValue)
            query = query.Where(e => e.Severity == request.Severity.Value);

        if (request.CharacteristicId.HasValue)
            query = query.Where(e => e.CharacteristicId == request.CharacteristicId.Value);

        var events = await query
            .OrderByDescending(e => e.DetectedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        // Resolve acknowledged-by user names
        var acknowledgedByIds = events
            .Where(e => e.AcknowledgedById.HasValue)
            .Select(e => e.AcknowledgedById!.Value)
            .Distinct()
            .ToList();

        var userNames = acknowledgedByIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => acknowledgedByIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.LastName + ", " + u.FirstName, cancellationToken)
            : new Dictionary<int, string>();

        return events.Select(e => new SpcOocEventResponseModel
        {
            Id = e.Id,
            CharacteristicId = e.CharacteristicId,
            CharacteristicName = e.Characteristic.Name,
            PartNumber = e.Characteristic.Part.PartNumber,
            MeasurementId = e.MeasurementId,
            DetectedAt = e.DetectedAt,
            RuleName = e.RuleName,
            Description = e.Description,
            Severity = e.Severity.ToString(),
            Status = e.Status.ToString(),
            AcknowledgedByName = e.AcknowledgedById.HasValue
                ? userNames.GetValueOrDefault(e.AcknowledgedById.Value)
                : null,
            AcknowledgedAt = e.AcknowledgedAt,
            AcknowledgmentNotes = e.AcknowledgmentNotes,
            CapaId = e.CapaId,
        }).ToList();
    }
}
