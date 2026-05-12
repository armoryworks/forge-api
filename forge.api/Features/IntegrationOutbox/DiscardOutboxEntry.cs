using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.IntegrationOutbox;

public record DiscardOutboxEntryCommand(int Id) : IRequest;

public class DiscardOutboxEntryHandler(AppDbContext db)
    : IRequestHandler<DiscardOutboxEntryCommand>
{
    public async Task Handle(DiscardOutboxEntryCommand request, CancellationToken ct)
    {
        var entry = await db.IntegrationOutboxEntries
            .FirstOrDefaultAsync(e => e.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Outbox entry {request.Id} not found");

        if (entry.Status == OutboxStatus.Sent)
        {
            throw new InvalidOperationException("Cannot discard an entry that has already been sent");
        }

        entry.Status = OutboxStatus.DeadLetter;
        entry.NextAttemptAt = null;
        entry.LastError = (entry.LastError ?? string.Empty) + " [discarded by admin]";

        await db.SaveChangesAsync(ct);
    }
}
