using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Customers.Segments;

// C3: soft-delete a customer segment.
public sealed record DeleteCustomerSegmentCommand(int Id) : IRequest;

public sealed class DeleteCustomerSegmentHandler(AppDbContext db) : IRequestHandler<DeleteCustomerSegmentCommand>
{
    public async Task Handle(DeleteCustomerSegmentCommand request, CancellationToken ct)
    {
        var segment = await db.CustomerSegments.FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Customer segment {request.Id} not found");

        segment.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
