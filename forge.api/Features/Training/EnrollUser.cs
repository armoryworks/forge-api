using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

public record EnrollUserCommand(int UserId, int PathId, int AssignedByUserId) : IRequest;

public class EnrollUserHandler(AppDbContext db) : IRequestHandler<EnrollUserCommand>
{
    public async Task Handle(EnrollUserCommand request, CancellationToken ct)
    {
        var path = await db.TrainingPaths.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PathId, ct)
            ?? throw new KeyNotFoundException($"Training path {request.PathId} not found.");

        var existing = await db.TrainingPathEnrollments
            .FirstOrDefaultAsync(e => e.UserId == request.UserId && e.PathId == request.PathId, ct);

        if (existing is not null)
            throw new InvalidOperationException($"User {request.UserId} is already enrolled in path {request.PathId}.");

        db.TrainingPathEnrollments.Add(new TrainingPathEnrollment
        {
            UserId = request.UserId,
            PathId = request.PathId,
            IsAutoAssigned = false,
            AssignedByUserId = request.AssignedByUserId,
        });

        await db.SaveChangesAsync(ct);
    }
}
