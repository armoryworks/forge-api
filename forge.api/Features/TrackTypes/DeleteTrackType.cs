using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.TrackTypes;

public record DeleteTrackTypeCommand(int Id) : IRequest;

public class DeleteTrackTypeHandler(
    ITrackTypeRepository repo) : IRequestHandler<DeleteTrackTypeCommand>
{
    public async Task Handle(DeleteTrackTypeCommand request, CancellationToken ct)
    {
        var trackType = await repo.FindAsync(request.Id, ct)
            ?? throw new KeyNotFoundException($"Track type with ID {request.Id} not found.");

        if (trackType.IsDefault)
            throw new InvalidOperationException("Cannot delete the default track type.");

        trackType.IsActive = false;
        foreach (var stage in trackType.Stages)
            stage.IsActive = false;

        await repo.SaveChangesAsync(ct);
    }
}
