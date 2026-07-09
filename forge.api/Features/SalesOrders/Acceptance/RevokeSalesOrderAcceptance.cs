using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// Revoke an acceptance record (admin) — e.g. recorded in error or the terms materially changed.
/// Revoking the only Accepted record re-closes the production gate. Admin-gated at the controller.
/// </summary>
public record RevokeSalesOrderAcceptanceCommand(int SalesOrderId, int AcceptanceId, string? Reason) : IRequest;

public class RevokeSalesOrderAcceptanceHandler(AppDbContext db) : IRequestHandler<RevokeSalesOrderAcceptanceCommand>
{
    public async Task Handle(RevokeSalesOrderAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var acceptance = await db.SalesOrderAcceptances
            .FirstOrDefaultAsync(a => a.Id == request.AcceptanceId && a.SalesOrderId == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Acceptance {request.AcceptanceId} not found on sales order {request.SalesOrderId}.");

        if (acceptance.Status == AcceptanceStatus.Revoked)
            return;

        acceptance.Status = AcceptanceStatus.Revoked;
        if (!string.IsNullOrWhiteSpace(request.Reason))
            acceptance.Note = string.IsNullOrWhiteSpace(acceptance.Note)
                ? $"Revoked: {request.Reason}"
                : $"{acceptance.Note} · Revoked: {request.Reason}";

        db.LogActivityAt("so-acceptance-revoked", "Customer acceptance revoked", ("SalesOrder", request.SalesOrderId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
