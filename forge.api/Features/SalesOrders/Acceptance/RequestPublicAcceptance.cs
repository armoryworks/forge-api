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
/// Public accept portal channel (staff side) — mint a Pending PublicPortal acceptance with an unguessable
/// link token and a hashed second key the customer must also prove. Returns the token so the UI can build
/// the shareable link (emailing it is a follow-up). The customer accepts on the public page, which flips
/// the record to Accepted (see SubmitPublicAcceptance).
/// </summary>
public record RequestPublicAcceptanceCommand(int SalesOrderId, string RecipientEmail, string VerificationKey, int ValidDays = 14)
    : IRequest<RequestPublicAcceptanceResponseModel>;

public record RequestPublicAcceptanceResponseModel(SalesOrderAcceptanceResponseModel Acceptance, string Token);

public class RequestPublicAcceptanceValidator : AbstractValidator<RequestPublicAcceptanceCommand>
{
    public RequestPublicAcceptanceValidator()
    {
        RuleFor(x => x.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.RecipientEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.VerificationKey)
            .NotEmpty().WithMessage("A verification key the customer can prove (e.g. their PO number) is required.")
            .MinimumLength(3);
        RuleFor(x => x.ValidDays).InclusiveBetween(1, 90);
    }
}

public class RequestPublicAcceptanceHandler(AppDbContext db, IClock clock)
    : IRequestHandler<RequestPublicAcceptanceCommand, RequestPublicAcceptanceResponseModel>
{
    public async Task<RequestPublicAcceptanceResponseModel> Handle(RequestPublicAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var soExists = await db.SalesOrders.AnyAsync(o => o.Id == request.SalesOrderId, cancellationToken);
        if (!soExists)
            throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found.");

        var token = AcceptancePortalCrypto.GenerateToken();
        var acceptance = new SalesOrderAcceptance
        {
            SalesOrderId = request.SalesOrderId,
            Status = AcceptanceStatus.Pending,
            Method = AcceptanceMethod.PublicPortal,
            AccessToken = token,
            VerificationKeyHash = AcceptancePortalCrypto.HashKey(request.VerificationKey),
            SentTo = request.RecipientEmail,
            RecordedByUserId = db.CurrentUserId,
            ExpiresAt = clock.UtcNow.AddDays(request.ValidDays),
        };
        db.SalesOrderAcceptances.Add(acceptance);
        db.LogActivityAt("so-acceptance-requested",
            $"Public acceptance link issued to {request.RecipientEmail}", ("SalesOrder", request.SalesOrderId));
        await db.SaveChangesAsync(cancellationToken);

        var model = await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);
        return new RequestPublicAcceptanceResponseModel(model, token);
    }
}
