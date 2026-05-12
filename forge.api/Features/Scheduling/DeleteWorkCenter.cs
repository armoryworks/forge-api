using MediatR;

using Forge.Data.Context;

namespace Forge.Api.Features.Scheduling;

public record DeleteWorkCenterCommand(int Id) : IRequest;

public class DeleteWorkCenterHandler(AppDbContext db) : IRequestHandler<DeleteWorkCenterCommand>
{
    public async Task Handle(DeleteWorkCenterCommand request, CancellationToken cancellationToken)
    {
        var wc = await db.WorkCenters.FindAsync([request.Id], cancellationToken)
            ?? throw new KeyNotFoundException($"Work center {request.Id} not found.");

        wc.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
