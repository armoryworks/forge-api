using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// Email-ingest channel (seam). An inbound email matched to a Sales Order — from the eventual system
/// mailbox, or registered by staff — creates a Pending Email acceptance for review. Kept Pending (not
/// auto-Accepted) because inbound matching is heuristic: a human confirms it via ConfirmEmailAcceptance,
/// optionally attaching the emailed document. Ready for the mailbox processor to call directly.
/// </summary>
public record IngestEmailAcceptanceCommand(int SalesOrderId, string FromEmail, string? Note)
    : IRequest<SalesOrderAcceptanceResponseModel>;

public class IngestEmailAcceptanceValidator : AbstractValidator<IngestEmailAcceptanceCommand>
{
    public IngestEmailAcceptanceValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.FromEmail).NotEmpty().EmailAddress();
    }
}

public class IngestEmailAcceptanceHandler(AppDbContext db) : IRequestHandler<IngestEmailAcceptanceCommand, SalesOrderAcceptanceResponseModel>
{
    public async Task<SalesOrderAcceptanceResponseModel> Handle(IngestEmailAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var soExists = await db.SalesOrders.AnyAsync(o => o.Id == request.SalesOrderId, cancellationToken);
        if (!soExists)
            throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found.");

        var acceptance = new SalesOrderAcceptance
        {
            SalesOrderId = request.SalesOrderId,
            Status = AcceptanceStatus.Pending,
            Method = AcceptanceMethod.Email,
            SentTo = request.FromEmail,
            Note = request.Note ?? $"Inbound email from {request.FromEmail} awaiting review.",
        };
        db.SalesOrderAcceptances.Add(acceptance);
        db.LogActivityAt("so-acceptance-email-ingested",
            $"Inbound acceptance email from {request.FromEmail} queued for review", ("SalesOrder", request.SalesOrderId));
        await db.SaveChangesAsync(cancellationToken);

        return await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);
    }
}
