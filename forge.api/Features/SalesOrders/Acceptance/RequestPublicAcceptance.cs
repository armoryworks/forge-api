using System.Globalization;
using System.Net;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders.Acceptance;

/// <summary>
/// Public accept portal channel (staff side) — mint a Pending PublicPortal acceptance with an unguessable
/// link token and a hashed second key the customer must also prove, and queue an e-mail with the link to
/// the recipient in English or Spanish (<paramref name="Language"/>, English by default), once per token.
/// Returns the token so the UI can also show the shareable link. The customer accepts on the public page,
/// which flips the record to Accepted (see SubmitPublicAcceptance).
/// </summary>
public record RequestPublicAcceptanceCommand(
    int SalesOrderId, string RecipientEmail, string VerificationKey, int ValidDays = 14, string? Language = null)
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
        RuleFor(x => x.Language)
            .Must(l => l is null || RequestPublicAcceptanceHandler.SupportedLanguages.Contains(l.Trim().ToLowerInvariant()))
            .WithMessage("Language must be en or es.");
    }
}

public class RequestPublicAcceptanceHandler(
    AppDbContext db,
    IClock clock,
    IIntegrationOutboxService outbox,
    IConfiguration config,
    IHttpContextAccessor httpContext)
    : IRequestHandler<RequestPublicAcceptanceCommand, RequestPublicAcceptanceResponseModel>
{
    public static readonly string[] SupportedLanguages = ["en", "es"];

    public async Task<RequestPublicAcceptanceResponseModel> Handle(RequestPublicAcceptanceCommand request, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == request.SalesOrderId)
            .Select(o => new { o.OrderNumber })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found.");

        var baseUrl = httpContext.HttpContext is { } http
            ? FrontendBaseUrl.Resolve(config, http.Request)
            : config[FrontendBaseUrl.SettingKey]?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException(
                $"Set {FrontendBaseUrl.SettingKey} so the acceptance link can be e-mailed to the customer.");

        var token = AcceptancePortalCrypto.GenerateToken();
        var acceptance = new Attestation
        {
            SalesOrderId = request.SalesOrderId,
            Status = AcceptanceStatus.Pending,
            Method = AcceptanceMethod.PublicPortal,
            AccessToken = token,
            VerificationKeyHash = AcceptancePortalCrypto.HashKey(request.VerificationKey),
            SentTo = request.RecipientEmail.Trim(),
            RecordedByUserId = db.CurrentUserId,
            ExpiresAt = clock.UtcNow.AddDays(request.ValidDays),
        };
        db.Attestations.Add(acceptance);
        db.LogActivityAt("so-acceptance-requested",
            $"Acceptance link e-mailed to {request.RecipientEmail.Trim()}", ("SalesOrder", request.SalesOrderId));
        await db.SaveChangesAsync(cancellationToken);

        await QueueEmailAsync(acceptance, $"{baseUrl}/accept/{Uri.EscapeDataString(token)}", order.OrderNumber, request.Language, cancellationToken);

        var model = await AcceptanceQuery.ByIdAsync(db, acceptance.Id, cancellationToken);
        return new RequestPublicAcceptanceResponseModel(model, token);
    }

    private async Task QueueEmailAsync(Attestation acceptance, string link, string orderNumber, string? language, CancellationToken ct)
    {
        var company = await CompanyIdentity.GetCompanyNameAsync(db, ct);
        var spanish = string.Equals(language?.Trim(), "es", StringComparison.OrdinalIgnoreCase);
        var expires = acceptance.ExpiresAt!.Value.UtcDateTime.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        var message = spanish
            ? BuildMessage(acceptance.SentTo!, link, orderNumber, company,
                subject: company is null ? $"Acepte su pedido {orderNumber}" : $"Acepte su pedido {orderNumber} de {company}",
                intro: "Revise y acepte su pedido en línea con el siguiente enlace.",
                action: "Revisar y aceptar el pedido",
                keyHint: "Se le pedirá la clave de verificación que acordamos con usted (por ejemplo, su número de orden de compra).",
                expiry: $"El enlace vence el {expires}.")
            : BuildMessage(acceptance.SentTo!, link, orderNumber, company,
                subject: company is null ? $"Please accept order {orderNumber}" : $"Please accept order {orderNumber} from {company}",
                intro: "Please review and accept your order online using the link below.",
                action: "Review and accept the order",
                keyHint: "You will be asked for the verification key we agreed with you (for example, your purchase order number).",
                expiry: $"The link expires on {expires}.");

        await outbox.EnqueueEmailAsync(
            $"so-acceptance-email:{acceptance.Id}", message, entityType: "SalesOrder", entityId: acceptance.SalesOrderId, ct: ct);
    }

    private static EmailMessage BuildMessage(
        string to, string link, string orderNumber, string? company,
        string subject, string intro, string action, string keyHint, string expiry)
    {
        var html =
            $"<h2>{WebUtility.HtmlEncode(orderNumber)}</h2>" +
            $"<p>{WebUtility.HtmlEncode(intro)}</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(action)}</a></p>" +
            $"<p>{WebUtility.HtmlEncode(keyHint)}</p>" +
            $"<p>{WebUtility.HtmlEncode(expiry)}</p>" +
            (company is null ? "" : $"<p>— {WebUtility.HtmlEncode(company)}</p>");
        var text = $"{orderNumber}\n\n{intro}\n{link}\n\n{keyHint}\n{expiry}\n" + (company is null ? "" : $"\n— {company}\n");
        return new EmailMessage(To: to, Subject: subject, HtmlBody: html, PlainTextBody: text);
    }
}
