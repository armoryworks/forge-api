using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.ShiftAssignments;

public record DeleteShiftAssignmentCommand(int Id) : IRequest;

public class DeleteShiftAssignmentHandler(AppDbContext db) : IRequestHandler<DeleteShiftAssignmentCommand>
{
    public async Task Handle(DeleteShiftAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await db.ShiftAssignments.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Shift assignment {request.Id} not found");

        db.ShiftAssignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
    }
}
