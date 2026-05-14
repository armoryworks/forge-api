using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

public record ApproveEcoCommand(int Id) : IRequest;

public class ApproveEcoHandler(AppDbContext db, IHttpContextAccessor httpContext, IClock clock)
    : IRequestHandler<ApproveEcoCommand>
{
    public async Task Handle(ApproveEcoCommand request, CancellationToken cancellationToken)
    {
        var eco = await db.EngineeringChangeOrders
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"ECO {request.Id} not found");

        if (eco.Status != EcoStatus.Review)
            throw new InvalidOperationException("ECO must be in Review status to approve");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        eco.Status = EcoStatus.Approved;
        eco.ApprovedById = userId;
        eco.ApprovedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
