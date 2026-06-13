using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.Qbo;

/// <summary>
/// QB-001 — remove a GL→QBO mapping (soft delete; the filtered unique index
/// lets the account be re-mapped later). No activity log per GL-subsystem
/// precedent.
/// </summary>
[RequiresCapability("CAP-ACCT-QBO-EXPORT")]
public record DeleteQboAccountMapCommand(int GlAccountId) : IRequest;

public class DeleteQboAccountMapHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteQboAccountMapCommand>
{
    public async Task Handle(DeleteQboAccountMapCommand request, CancellationToken cancellationToken)
    {
        var map = await db.QboAccountMaps
            .FirstOrDefaultAsync(m => m.GlAccountId == request.GlAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"No QuickBooks mapping exists for GL account {request.GlAccountId}.");

        map.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
