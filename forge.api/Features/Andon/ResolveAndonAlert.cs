using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Andon;

public record ResolveAndonAlertCommand(int Id, string? Notes) : IRequest;

public class ResolveAndonAlertHandler(AppDbContext db, IClock clock, IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<ResolveAndonAlertCommand>
{
    public async Task Handle(ResolveAndonAlertCommand request, CancellationToken cancellationToken)
    {
        var alert = await db.AndonAlerts
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"AndonAlert {request.Id} not found");

        if (alert.Status == AndonAlertStatus.Resolved)
            throw new InvalidOperationException("Alert is already resolved");

        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        alert.Status = AndonAlertStatus.Resolved;
        alert.ResolvedById = userId;
        alert.ResolvedAt = clock.UtcNow;

        if (!string.IsNullOrEmpty(request.Notes))
            alert.Notes = string.IsNullOrEmpty(alert.Notes) ? request.Notes : $"{alert.Notes}\n{request.Notes}";

        await db.SaveChangesAsync(cancellationToken);
    }
}
