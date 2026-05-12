using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.TimeTracking;

public sealed record DeleteTimeEntryCommand(int Id) : IRequest;

public sealed class DeleteTimeEntryHandler(ITimeTrackingRepository repo)
    : IRequestHandler<DeleteTimeEntryCommand>
{
    public async Task Handle(DeleteTimeEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindTimeEntryAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Time entry {request.Id} not found");

        if (entry.IsLocked)
            throw new InvalidOperationException("Locked time entries cannot be deleted.");

        if (entry.Date < DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime))
            throw new InvalidOperationException("Time entries from previous days cannot be deleted.");

        entry.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
