using MediatR;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Parts;

public sealed record DeletePartCommand(int Id) : IRequest;

public sealed class DeletePartHandler(IPartRepository repo, AppDbContext db, IClock clock)
    : IRequestHandler<DeletePartCommand>
{
    public async Task Handle(DeletePartCommand request, CancellationToken cancellationToken)
    {
        var part = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.Id} not found");

        part.DeletedAt = clock.UtcNow;

        db.LogActivityAt(
            "deleted",
            $"Deleted part: {part.PartNumber} — {part.Name}",
            ("Part", part.Id));

        await repo.SaveChangesAsync(cancellationToken);
    }
}
