using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Training;

public record UpdateTrainingEnrollmentCommand(int Id, DateOnly? DueDate) : IRequest;

public class UpdateTrainingEnrollmentHandler(AppDbContext db) : IRequestHandler<UpdateTrainingEnrollmentCommand>
{
    public async Task Handle(UpdateTrainingEnrollmentCommand request, CancellationToken ct)
    {
        var enrollment = await db.TrainingPathEnrollments.FirstOrDefaultAsync(e => e.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Enrollment {request.Id} not found.");

        enrollment.DueDate = request.DueDate;
        db.LogActivityAt(
            "enrollment-updated",
            request.DueDate is DateOnly due ? $"Due date set to {due:yyyy-MM-dd} for user {enrollment.UserId}" : $"Due date cleared for user {enrollment.UserId}",
            ("TrainingPath", enrollment.PathId));
        await db.SaveChangesAsync(ct);
    }
}
