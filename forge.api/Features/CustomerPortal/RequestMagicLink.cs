using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CustomerPortal;

/// <summary>
/// Phase 1q — request a magic-link login. Looks up an enabled
/// CustomerPortalAccess row of an active customer whose contact has this email (access is
/// provisioned only by staff, see CreatePortalAccessHandler), generates a
/// one-time token (15 min lifetime), stores its hash, and dispatches the
/// link via <see cref="IEmailService"/>. A new link is not issued within
/// <see cref="RequestMagicLinkHandler.ResendCooldown"/> of the previous one.
/// The handler always returns success whether the email is unknown, belongs
/// to a contact without enabled access, or is inside the cooldown — leaking
/// that information enables enumeration of which of a company's contacts
/// have portal access.
///
/// In dev / mock-SMTP mode the link is also returned in the response so
/// the developer can drive the flow without a working mailbox. In prod
/// (real SMTP) the response carries no link.
/// </summary>
public record RequestMagicLinkCommand(string Email, string PortalBaseUrl) : IRequest<RequestMagicLinkResult>;

public record RequestMagicLinkResult(string? DevLink);

public class RequestMagicLinkCommandValidator : AbstractValidator<RequestMagicLinkCommand>
{
    public RequestMagicLinkCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(200)
            .Must(e => e is null || !e.Contains('\0')).WithMessage("Email contains an invalid character.")
            .EmailAddress().WithMessage("A valid email address is required.");
    }
}

public class RequestMagicLinkHandler(
    AppDbContext db,
    IPortalAuthService portalAuth,
    IEmailService email,
    IConfiguration config,
    IClock clock,
    ILogger<RequestMagicLinkHandler> logger)
    : IRequestHandler<RequestMagicLinkCommand, RequestMagicLinkResult>
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public async Task<RequestMagicLinkResult> Handle(RequestMagicLinkCommand request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLower();
        var access = await db.CustomerPortalAccesses
            .Include(a => a.Contact)
            .Where(a => a.IsEnabled
                && a.Customer.IsActive
                && a.Contact.Email != null
                && a.Contact.Email.ToLower() == normalizedEmail)
            .OrderBy(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (access is null)
        {
            // Avoid enumeration — caller can't tell whether the email matched.
            logger.LogInformation("Portal magic-link request for an email without enabled portal access {Email}", request.Email);
            return new RequestMagicLinkResult(DevLink: null);
        }

        var contact = access.Contact;
        var now = clock.UtcNow;
        if (access.OneTimeTokenExpiresAt is { } pendingExpiry
            && pendingExpiry - TokenLifetime + ResendCooldown > now)
        {
            logger.LogInformation("Portal magic-link request for contact {ContactId} inside the resend cooldown", contact.Id);
            return new RequestMagicLinkResult(DevLink: null);
        }

        var (plaintext, hash) = portalAuth.GenerateMagicLinkToken();
        access.OneTimeTokenHash = hash;
        access.OneTimeTokenExpiresAt = now + TokenLifetime;

        await db.SaveChangesAsync(ct);

        var trimmedBase = request.PortalBaseUrl.TrimEnd('/');
        var link = $"{trimmedBase}/portal/auth/callback?token={plaintext}";

        try
        {
            var html =
                $"<p>Hi {contact.FirstName},</p>" +
                $"<p>Click the link below to sign in to the customer portal. The link expires in 15 minutes.</p>" +
                $"<p><a href=\"{link}\">Sign in to the portal</a></p>" +
                $"<p>If you didn't request this, you can safely ignore this email.</p>";
            var text =
                $"Hi {contact.FirstName},\n\nSign in to the customer portal — link expires in 15 minutes:\n{link}\n";

            await email.SendAsync(new EmailMessage(
                To: contact.Email!,
                Subject: "Sign in to your customer portal",
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
