using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record DeleteShiftCommand(int Id) : IRequest;

public class DeleteShiftHandler(AppDbContext db) : IRequestHandler<DeleteShiftCommand>
{
    public async Task Handle(DeleteShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await db.Shifts.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Shift {request.Id} not found.");

        shift.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
