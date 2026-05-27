using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Lots;

// L2: lots had no archive path despite the DeletedAt column. Soft-delete a mistaken lot.
public sealed record DeleteLotRecordCommand(int Id) : IRequest;

public sealed class DeleteLotRecordHandler(AppDbContext db) : IRequestHandler<DeleteLotRecordCommand>
{
    public async Task Handle(DeleteLotRecordCommand request, CancellationToken cancellationToken)
    {
        var lot = await db.LotRecords.FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Lot {request.Id} not found");

        lot.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
