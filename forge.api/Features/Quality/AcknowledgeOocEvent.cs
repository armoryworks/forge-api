using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record AcknowledgeOocEventCommand(int Id, AcknowledgeOocRequestModel Data) : IRequest<SpcOocEventResponseModel>;

public class AcknowledgeOocEventHandler(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<AcknowledgeOocEventCommand, SpcOocEventResponseModel>
{
    public async Task<SpcOocEventResponseModel> Handle(
        AcknowledgeOocEventCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var oocEvent = await db.SpcOocEvents
            .Include(e => e.Characteristic)
            .ThenInclude(c => c.Part)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"OOC Event {request.Id} not found.");

        if (oocEvent.Status != SpcOocStatus.Open)
            throw new InvalidOperationException($"OOC Event {request.Id} is already {oocEvent.Status}.");

        oocEvent.Status = SpcOocStatus.Acknowledged;
        oocEvent.AcknowledgedById = userId;
        oocEvent.AcknowledgedAt = DateTimeOffset.UtcNow;
        oocEvent.AcknowledgmentNotes = request.Data.Notes?.Trim();

        await db.SaveChangesAsync(cancellationToken);

        var userName = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.LastName + ", " + u.FirstName)
            .FirstOrDefaultAsync(cancellationToken) ?? "";

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
            AcknowledgedByName = userName,
            AcknowledgedAt = oocEvent.AcknowledgedAt,
            AcknowledgmentNotes = oocEvent.AcknowledgmentNotes,
            CapaId = oocEvent.CapaId,
        };
    }
}
