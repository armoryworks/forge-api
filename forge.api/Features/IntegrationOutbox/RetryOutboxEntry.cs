using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.IntegrationOutbox;

public record RetryOutboxEntryCommand(int Id) : IRequest;

public class RetryOutboxEntryHandler(AppDbContext db, IClock clock)
    : IRequestHandler<RetryOutboxEntryCommand>
{
    public async Task Handle(RetryOutboxEntryCommand request, CancellationToken ct)
    {
        var entry = await db.IntegrationOutboxEntries
            .FirstOrDefaultAsync(e => e.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Outbox entry {request.Id} not found");

        if (entry.Status == OutboxStatus.Sent)
        {
            throw new InvalidOperationException("Cannot retry an entry that has already been sent");
        }

        entry.Status = OutboxStatus.Pending;
        entry.NextAttemptAt = clock.UtcNow;
        entry.LastError = null;

        // Reset attempt count for dead-lettered entries so the normal backoff/max-attempt policy applies again
        if (entry.AttemptCount >= entry.MaxAttempts)
        {
            entry.AttemptCount = 0;
        }

        await db.SaveChangesAsync(ct);
    }
}
