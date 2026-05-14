using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record ImplementEcoCommand(int Id) : IRequest;

public class ImplementEcoHandler(AppDbContext db, IHttpContextAccessor httpContext, IClock clock)
    : IRequestHandler<ImplementEcoCommand>
{
    public async Task Handle(ImplementEcoCommand request, CancellationToken cancellationToken)
    {
        var eco = await db.EngineeringChangeOrders
            .Include(e => e.AffectedItems)
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.Id} not found");

        if (eco.Status != EcoStatus.Approved && eco.Status != EcoStatus.InImplementation)
            throw new InvalidOperationException("ECO must be in Approved or InImplementation status");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Mark all affected items as implemented
        foreach (var item in eco.AffectedItems)
        {
            item.IsImplemented = true;
        }

        eco.Status = EcoStatus.Implemented;
        eco.ImplementedAt = clock.UtcNow;
        eco.ImplementedById = userId;

        await db.SaveChangesAsync(cancellationToken);
    }
}
