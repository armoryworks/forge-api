using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateCapaFromOocCommand(int OocEventId, int? OwnerId = null) : IRequest<SpcOocEventResponseModel>;

public class CreateCapaFromOocHandler(
    AppDbContext db,
    INcrCapaService ncrCapaService,
    IClock clock,
    IHttpContextAccessor httpContextAccessor)
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

        if (oocEvent.CapaId is null)
        {
            var ownerId = request.OwnerId
                ?? int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var characteristic = oocEvent.Characteristic;

            await using var tx = db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var capa = new CorrectiveAction
            {
                CapaNumber = await ncrCapaService.GenerateCapaNumberAsync(cancellationToken),
                Type = CapaType.Corrective,
                SourceType = CapaSourceType.SpcOoc,
                SourceEntityId = oocEvent.Id,
                SourceEntityType = "SpcOocEvent",
                Title = $"CAPA for SPC {oocEvent.RuleName} — {characteristic.Part.PartNumber} {characteristic.Name}",
                ProblemDescription = oocEvent.Description,
                OwnerId = ownerId,
                Status = CapaStatus.Open,
                Priority = 2,
                DueDate = clock.UtcNow.AddDays(30),
            };
            db.CorrectiveActions.Add(capa);
            await db.SaveChangesAsync(cancellationToken);

            oocEvent.CapaId = capa.Id;
            oocEvent.Status = SpcOocStatus.CapaCreated;
            await db.SaveChangesAsync(cancellationToken);

            if (tx is not null)
                await tx.CommitAsync(cancellationToken);
        }

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
