using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// Staff confirms a Pending Email acceptance (from the ingest seam) after eyeballing it — flips it to
/// Accepted and stamps the reviewer. Only applies to Pending Email records; the self-service channels
/// (portal / e-signature) complete through their own flows.
/// </summary>
public record ConfirmEmailAcceptanceCommand(int SalesOrderId, int AcceptanceId)
    : IRequest<SalesOrderAcceptanceResponseModel>;

public class ConfirmEmailAcceptanceHandler(AppDbContext db, IClock clock)
    : IRequestHandler<ConfirmEmailAcceptanceCommand, SalesOrderAcceptanceResponseModel>
{
    public async Task<SalesOrderAcceptanceResponseModel> Handle(ConfirmEmailAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var acceptance = await db.SalesOrderAcceptances
            .FirstOrDefaultAsync(a => a.Id == request.AcceptanceId && a.SalesOrderId == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Acceptance {request.AcceptanceId} not found on sales order {request.SalesOrderId}.");

        if (acceptance.Method != AcceptanceMethod.Email || acceptance.Status != AcceptanceStatus.Pending)
            throw new InvalidOperationException("Only a pending inbound-email acceptance can be confirmed here.");

        acceptance.Status = AcceptanceStatus.Accepted;
        acceptance.RecordedByUserId = db.CurrentUserId;
        acceptance.AcceptedAt = clock.UtcNow;

        db.LogActivityAt("so-acceptance-recorded",
            "Inbound-email customer acceptance confirmed by staff", ("SalesOrder", request.SalesOrderId));
        await db.SaveChangesAsync(cancellationToken);

        return await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);
    }
}
