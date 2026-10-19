using System.Net;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — request a magic-link login. Looks up the contact by email,
/// ensures a CustomerPortalAccess row exists for them with IsEnabled=true,
/// generates a one-time token (15 min lifetime), stores its hash, and
/// dispatches the link via <see cref="IEmailService"/>. The handler
/// always returns success regardless of whether the email matched a real
/// contact — leaking that information enables enumeration of which of a
/// company's contacts have portal access.
///
/// In dev / mock-SMTP mode the link is also returned in the response so
/// the developer can drive the flow without a working mailbox. In prod
/// (real SMTP) the response carries no link.
/// </summary>
public record RequestMagicLinkCommand(string Email, string PortalBaseUrl) : IRequest<RequestMagicLinkResult>;

public record RequestMagicLinkResult(string? DevLink);

public class RequestMagicLinkHandler(
    AppDbContext db,
    IPortalAuthService portalAuth,
    IEmailService email,
    IConfiguration config,
    ILogger<RequestMagicLinkHandler> logger)
    : IRequestHandler<RequestMagicLinkCommand, RequestMagicLinkResult>
{
    public async Task<RequestMagicLinkResult> Handle(RequestMagicLinkCommand request, CancellationToken ct)
    {
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Email != null && c.Email.ToLower() == request.Email.ToLower(), ct);

        if (contact is null)
        {
            // Avoid enumeration — caller can't tell whether the email matched.
            logger.LogInformation("Portal magic-link request for unknown email {Email}", request.Email);
            return new RequestMagicLinkResult(DevLink: null);
        }

        var access = await db.CustomerPortalAccesses
            .FirstOrDefaultAsync(a => a.ContactId == contact.Id, ct);

        if (access is null)
        {
            access = new CustomerPortalAccess
            {
                ContactId = contact.Id,
                CustomerId = contact.CustomerId,
                IsEnabled = true,
            };
            db.CustomerPortalAccesses.Add(access);
        }

        if (!access.IsEnabled)
        {
            logger.LogInformation("Portal magic-link request for disabled contact {ContactId}", contact.Id);
            return new RequestMagicLinkResult(DevLink: null);
        }

        var (plaintext, hash) = portalAuth.GenerateMagicLinkToken();
        access.OneTimeTokenHash = hash;
        access.OneTimeTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);

        await db.SaveChangesAsync(ct);

        var trimmedBase = request.PortalBaseUrl.TrimEnd('/');
        var link = $"{trimmedBase}/portal/auth/callback?token={plaintext}";

        try
        {
            var company = await CompanyIdentity.GetCompanyNameAsync(db, ct);
            var portalName = company is null ? "the customer portal" : $"the {company} customer portal";
            var htmlPortalName = company is null ? portalName : $"the {WebUtility.HtmlEncode(company)} customer portal";
            var html =
                $"<p>Hi {contact.FirstName},</p>" +
                $"<p>Click the link below to sign in to {htmlPortalName}. The link expires in 15 minutes.</p>" +
                $"<p><a href=\"{link}\">Sign in to the portal</a></p>" +
                $"<p>If you didn't request this, you can safely ignore this email.</p>";
            var text =
                $"Hi {contact.FirstName},\n\nSign in to {portalName} — link expires in 15 minutes:\n{link}\n";

            await email.SendAsync(new EmailMessage(
                To: request.Email,
                Subject: company is null ? "Sign in to your customer portal" : $"Sign in to the {company} customer portal",
                HtmlBody: html,
                PlainTextBody: text), ct);
        }
        catch (Exception ex)
        {
            // Email failure shouldn't take down the request — the operator
            // can still surface the link from server logs in pre-SMTP setups.
            logger.LogWarning(ex, "Portal magic-link email send failed for {Email}", request.Email);
        }

        // Dev-mode convenience: when the SMTP mock or no SMTP is configured,
        // return the link in the response so the integrator can complete
        // the flow end-to-end without setting up a real mailbox.
        var smtpHost = config["Smtp:Host"];
        var devLink = string.IsNullOrWhiteSpace(smtpHost) ? link : null;

        logger.LogInformation("Portal magic-link issued to contact {ContactId}", contact.Id);
        return new RequestMagicLinkResult(devLink);
    }
}
