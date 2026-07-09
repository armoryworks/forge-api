using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// Public accept portal (customer side) — the customer proves the second key and accepts. Flips the
/// Pending record to Accepted, capturing their typed name + IP. Rejects expired links, replays
/// (already-responded), and a wrong verification key.
/// </summary>
public record SubmitPublicAcceptanceCommand(string Token, string? VerificationKey, string AcceptedByName, string? IpAddress)
    : IRequest;

public class SubmitPublicAcceptanceValidator : AbstractValidator<SubmitPublicAcceptanceCommand>
{
    public SubmitPublicAcceptanceValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.AcceptedByName)
            .NotEmpty().WithMessage("Please type your name to accept.")
            .MaximumLength(200);
    }
}

public class SubmitPublicAcceptanceHandler(AppDbContext db, IClock clock) : IRequestHandler<SubmitPublicAcceptanceCommand>
{
    public async Task Handle(SubmitPublicAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var acceptance = await db.SalesOrderAcceptances
            .FirstOrDefaultAsync(a => a.AccessToken == request.Token && a.Method == AcceptanceMethod.PublicPortal, cancellationToken)
            ?? throw new KeyNotFoundException("This acceptance link is not valid.");

        if (acceptance.Status != AcceptanceStatus.Pending)
            throw new InvalidOperationException("This order has already been responded to.");

        if (acceptance.ExpiresAt is not null && acceptance.ExpiresAt < clock.UtcNow)
        {
            acceptance.Status = AcceptanceStatus.Expired;
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("This acceptance link has expired. Please request a new one.");
        }

        if (!AcceptancePortalCrypto.KeyMatches(request.VerificationKey, acceptance.VerificationKeyHash))
            throw new InvalidOperationException("The verification key you entered doesn't match. Please check and try again.");

        acceptance.Status = AcceptanceStatus.Accepted;
        acceptance.AcceptedByName = request.AcceptedByName.Trim();
        acceptance.IpAddress = request.IpAddress;
        acceptance.AcceptedAt = clock.UtcNow;

        db.LogActivityAt("so-acceptance-recorded",
            $"Customer accepted online via portal ({request.AcceptedByName.Trim()})", ("SalesOrder", acceptance.SalesOrderId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
