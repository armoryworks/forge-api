using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Assets;

public sealed record DeleteAssetCommand(int Id) : IRequest;

public sealed class DeleteAssetHandler(IAssetRepository repo)
    : IRequestHandler<DeleteAssetCommand>
{
    public async Task Handle(DeleteAssetCommand request, CancellationToken cancellationToken)
    {
        var asset = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Asset {request.Id} not found");

        asset.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
