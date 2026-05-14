using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Estimates;

public record DeleteEstimateCommand(int Id) : IRequest;

public class DeleteEstimateHandler(AppDbContext db) : IRequestHandler<DeleteEstimateCommand>
{
    public async Task Handle(DeleteEstimateCommand request, CancellationToken ct)
    {
        var estimate = await db.Quotes
            .FirstOrDefaultAsync(q => q.Id == request.Id && q.Type == QuoteType.Estimate, ct)
            ?? throw new KeyNotFoundException($"Estimate {request.Id} not found.");

        if (estimate.DeletedAt != null)
            throw new KeyNotFoundException($"Estimate {request.Id} not found.");

        estimate.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
