using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Training;

public record RemoveTrainingEnrollmentCommand(int Id) : IRequest;

/// <summary>Removes an enrolment (soft delete). Module progress the user made is kept.</summary>
public class RemoveTrainingEnrollmentHandler(AppDbContext db, IClock clock) : IRequestHandler<RemoveTrainingEnrollmentCommand>
{
    public async Task Handle(RemoveTrainingEnrollmentCommand request, CancellationToken ct)
    {
        var enrollment = await db.TrainingPathEnrollments.FirstOrDefaultAsync(e => e.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Enrollment {request.Id} not found.");

        enrollment.DeletedAt = clock.UtcNow;
        db.LogActivityAt("unenrolled", $"User {enrollment.UserId} removed from the path", ("TrainingPath", enrollment.PathId));
        await db.SaveChangesAsync(ct);
    }
}
