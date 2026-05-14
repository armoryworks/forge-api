using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Andon;

public record AcknowledgeAndonAlertCommand(int Id) : IRequest;

public class AcknowledgeAndonAlertHandler(AppDbContext db, IClock clock, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<AcknowledgeAndonAlertCommand>
{
    public async Task Handle(AcknowledgeAndonAlertCommand request, CancellationToken cancellationToken)
    {
        var alert = await db.AndonAlerts
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"AndonAlert {request.Id} not found");

        if (alert.Status != AndonAlertStatus.Active)
            throw new InvalidOperationException("Only active alerts can be acknowledged");

        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        alert.Status = AndonAlertStatus.Acknowledged;
        alert.AcknowledgedById = userId;
        alert.AcknowledgedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
