using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Api.Middleware;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.TimeTracking;

public sealed record DeleteTimeEntryCommand(int Id) : IRequest;

public sealed class DeleteTimeEntryHandler(ITimeTrackingRepository repo, IHttpContextAccessor http)
    : IRequestHandler<DeleteTimeEntryCommand>
{
    public async Task Handle(DeleteTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindTimeEntryAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Time entry {request.Id} not found");

        // TT-01: only the owner — or a manager — may delete a time entry (no IDOR).
        var user = http.HttpContext?.User;
        var callerId = int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
        var isManager = user?.IsInRole("Admin") == true || user?.IsInRole("Manager") == true;
        if (entry.UserId != callerId && !isManager)
            throw new ForbiddenException("You can only delete your own time entries.");

        if (entry.IsLocked)
            throw new InvalidOperationException("Locked time entries cannot be deleted.");

        if (entry.Date < DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime))
            throw new InvalidOperationException("Time entries from previous days cannot be deleted.");

        entry.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
