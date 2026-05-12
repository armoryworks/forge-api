using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Edi;

public record DeleteEdiMappingCommand(int Id) : IRequest;

public class DeleteEdiMappingHandler(AppDbContext db)
    : IRequestHandler<DeleteEdiMappingCommand>
{
    public async Task Handle(DeleteEdiMappingCommand request, CancellationToken cancellationToken)
    {
        var mapping = await db.EdiMappings
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"EDI mapping {request.Id} not found");

        mapping.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
