using MediatR;
using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — exchanges a magic-link token for a portal session JWT. The
/// session token is signed with the same secret the employee scheme uses
/// but carries `portal_session=true`, `customer_id`, `contact_id` claims.
/// On exchange we clear the one-time token + stamp LastLoginAt so the
/// link is consumed (replays return 404).
/// </summary>
public record ExchangeMagicLinkCommand(string Token) : IRequest<PortalSessionResponseModel>;

public class ExchangeMagicLinkHandler(
    AppDbContext db,
    IPortalAuthService portalAuth,
    ITokenService tokens)
    : IRequestHandler<ExchangeMagicLinkCommand, PortalSessionResponseModel>
{
    private static readonly TimeSpan PortalSessionLifetime = TimeSpan.FromHours(2);

    public async Task<PortalSessionResponseModel> Handle(ExchangeMagicLinkCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new KeyNotFoundException("Token is missing or already used.");

        var hash = portalAuth.HashToken(request.Token);

        var access = await db.CustomerPortalAccesses
            .Include(a => a.Contact)
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a =>
                a.OneTimeTokenHash == hash &&
                a.IsEnabled &&
                a.OneTimeTokenExpiresAt != null &&
                a.OneTimeTokenExpiresAt > DateTimeOffset.UtcNow, ct)
            ?? throw new KeyNotFoundException("Token is invalid or expired.");

        // Burn the link — single-use.
        access.OneTimeTokenHash = null;
        access.OneTimeTokenExpiresAt = null;
        access.LastLoginAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        var token = tokens.GenerateToken(
            userId: access.ContactId,
            email: access.Contact.Email ?? string.Empty,
            firstName: access.Contact.FirstName,
            lastName: access.Contact.LastName,
            initials: $"{access.Contact.FirstName.FirstOrDefault()}{access.Contact.LastName.FirstOrDefault()}".ToUpper(),
            avatarColor: null,
            roles: new[] { "PortalUser" },
            expiry: PortalSessionLifetime,
            extraClaims: new Dictionary<string, string>
            {
                ["portal_session"] = "true",
                ["customer_id"] = access.CustomerId.ToString(),
                ["contact_id"] = access.ContactId.ToString(),
            });

        return new PortalSessionResponseModel(
            Token: token.Token,
            ExpiresAt: token.ExpiresAt,
            Identity: new PortalIdentityModel(
                ContactId: access.ContactId,
                CustomerId: access.CustomerId,
                CustomerName: access.Customer.Name,
                ContactFirstName: access.Contact.FirstName,
                ContactLastName: access.Contact.LastName,
                ContactEmail: access.Contact.Email));
    }
}
