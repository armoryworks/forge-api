using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record DeleteReferenceDataCommand(int Id) : IRequest;

public class DeleteReferenceDataHandler(AppDbContext db)
    : IRequestHandler<DeleteReferenceDataCommand>
{
    public async Task Handle(DeleteReferenceDataCommand request, CancellationToken cancellationToken)
    {
        var entry = await db.ReferenceData.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Reference data entry with ID {request.Id} not found.");

        if (entry.IsSeedData)
            throw new InvalidOperationException("Seed data entries cannot be deleted. You can deactivate them instead.");

        db.ReferenceData.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}
