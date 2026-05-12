using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record AcknowledgePredictionCommand(int Id) : IRequest;

public class AcknowledgePredictionHandler(AppDbContext db, IClock clock, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<AcknowledgePredictionCommand>
{
    public async Task Handle(AcknowledgePredictionCommand command, CancellationToken cancellationToken)
    {
        var prediction = await db.MaintenancePredictions
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Prediction {command.Id} not found");

        if (prediction.Status != MaintenancePredictionStatus.Predicted)
            throw new InvalidOperationException("Prediction has already been acknowledged");

        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        prediction.Status = MaintenancePredictionStatus.Acknowledged;
        prediction.AcknowledgedAt = clock.UtcNow;
        prediction.AcknowledgedByUserId = userId;

        await db.SaveChangesAsync(cancellationToken);
    }
}
