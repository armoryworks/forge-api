using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Inventory;

public sealed record DeleteStorageLocationCommand(int Id) : IRequest;

public sealed class DeleteStorageLocationHandler(IInventoryRepository repo)
    : IRequestHandler<DeleteStorageLocationCommand>
{
    public async Task Handle(DeleteStorageLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await repo.FindLocationAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Storage location {request.Id} not found");

        var contents = await repo.GetBinContentsAsync(request.Id, cancellationToken);
        if (contents.Count > 0)
            throw new InvalidOperationException("Cannot delete a location that contains items.");

        location.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
