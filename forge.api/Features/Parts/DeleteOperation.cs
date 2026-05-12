using MediatR;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Parts;

public sealed record DeleteOperationCommand(int PartId, int OperationId) : IRequest;

public sealed class DeleteOperationHandler(IPartRepository repo) : IRequestHandler<DeleteOperationCommand>
{
    public async Task Handle(DeleteOperationCommand request, CancellationToken cancellationToken)
    {
        var operation = await repo.FindOperationAsync(request.OperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.OperationId} not found");

        if (operation.PartId != request.PartId)
            throw new KeyNotFoundException($"Operation {request.OperationId} does not belong to part {request.PartId}");

        operation.DeletedAt = DateTimeOffset.UtcNow;
        await repo.SaveChangesAsync(cancellationToken);
    }
}
