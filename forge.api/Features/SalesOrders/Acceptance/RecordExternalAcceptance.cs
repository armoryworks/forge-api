using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// Channel: the customer's own system records acceptance against a Forge Sales Order (authenticated via
/// a System API key). Writes an Accepted record method=ExternalSystem carrying the caller's reference —
/// no document, the reference is the audit trail. Satisfies the production gate like any other channel.
/// </summary>
public record RecordExternalAcceptanceCommand(int SalesOrderId, string? Reference, string? AcceptedByName, string? Note)
    : IRequest<SalesOrderAcceptanceResponseModel>;

public class RecordExternalAcceptanceValidator : AbstractValidator<RecordExternalAcceptanceCommand>
{
    public RecordExternalAcceptanceValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.Reference)
            .NotEmpty().WithMessage("An external reference identifying the acceptance is required.")
            .MaximumLength(200);
    }
}

public class RecordExternalAcceptanceHandler(AppDbContext db, IClock clock)
    : IRequestHandler<RecordExternalAcceptanceCommand, SalesOrderAcceptanceResponseModel>
{
    public async Task<SalesOrderAcceptanceResponseModel> Handle(RecordExternalAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var soExists = await db.SalesOrders.AnyAsync(o => o.Id == request.SalesOrderId, cancellationToken);
        if (!soExists)
            throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found.");

        var acceptance = new SalesOrderAcceptance
        {
            SalesOrderId = request.SalesOrderId,
            Status = AcceptanceStatus.Accepted,
            Method = AcceptanceMethod.ExternalSystem,
            ProviderReference = request.Reference,
            AcceptedByName = request.AcceptedByName,
            RecordedByUserId = db.CurrentUserId,
            Note = request.Note,
            AcceptedAt = clock.UtcNow,
        };
        db.SalesOrderAcceptances.Add(acceptance);

        db.LogActivityAt("so-acceptance-recorded",
            $"Customer acceptance recorded by external system (ref {request.Reference})", ("SalesOrder", request.SalesOrderId));
        await db.SaveChangesAsync(cancellationToken);

        return await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);
    }
}
