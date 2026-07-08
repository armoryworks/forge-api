using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using QuestPDF.Fluent;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quotes;

/// <summary>
/// S3 — send the quote to a recipient by email: compiles the quote's dynamic
/// T&amp;C, persists an immutable <see cref="QuoteTermsSnapshot"/> (backing the
/// anonymous "view full terms" link), enqueues the email (PDF attached) on the
/// integration outbox with an idempotent operation key, then flips the quote
/// to Sent via <see cref="SendQuoteCommand"/> so one UI call does both.
/// Separate from <see cref="SendQuoteCommand"/>, which remains the plain
/// status-flip used when the quote is delivered out-of-band.
///
/// <para><c>PublicBaseUrl</c> is supplied by the controller from the incoming
/// request (scheme + host), mirroring the CustomerPortal magic-link pattern —
/// there is no app-wide public-API-base-URL option to reuse.</para>
/// </summary>
public record SendQuoteEmailCommand(
    int Id,
    string RecipientEmail,
    string? Message,
    string PublicBaseUrl) : IRequest;

public class SendQuoteEmailValidator : AbstractValidator<SendQuoteEmailCommand>
{
    public SendQuoteEmailValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.RecipientEmail).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Message).MaximumLength(4000);
        RuleFor(x => x.PublicBaseUrl).NotEmpty();
    }
}

public class SendQuoteEmailHandler(
    AppDbContext db,
    ISystemSettingRepository settings,
    IIntegrationOutboxService outbox,
    ITermsCompilationService compiler,
    IClock clock,
    IMediator mediator) : IRequestHandler<SendQuoteEmailCommand>
{
    public async Task Handle(SendQuoteEmailCommand request, CancellationToken ct)
    {
        var quote = await db.Quotes
            .Include(q => q.Customer)
            .Include(q => q.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(q => q.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        if (quote.Status != QuoteStatus.Draft && quote.Status != QuoteStatus.Sent)
            throw new InvalidOperationException("Only Draft or Sent quotes can be emailed");

        var companySetting = await settings.FindByKeyAsync("company_name", ct);
        var companyName = companySetting?.Value ?? "QB Engineer";

        // ── Compile terms + immutable snapshot ────────────────────────────
        var partIds = quote.Lines
            .Where(l => l.PartId.HasValue)
            .Select(l => l.PartId!.Value)
            .Distinct()
            .ToList();
        var compiled = await compiler.CompileForQuoteAsync(quote.CustomerId, partIds, ct);

        var accessToken = GenerateAccessToken();
        db.QuoteTermsSnapshots.Add(new QuoteTermsSnapshot
        {
            QuoteId = quote.Id,
            CompiledHtml = compiled.Html,
            CompiledSource = JsonSerializer.Serialize(compiled.Sections.Select(s => new
            {
                termsDocumentId = s.TermsDocumentId,
                version = s.Version,
                scope = s.Scope,
                title = s.Title,
            })),
            AccessToken = accessToken,
            SentTo = request.RecipientEmail,
        });

        // Quote is transactional — log on the quote itself only.
        db.LogActivityAt(
            "quote-email-sent",
            $"Quote emailed to {request.RecipientEmail}",
            ("Quote", quote.Id));

        await db.SaveChangesAsync(ct);

        // ── Email (PDF attached) via the idempotent outbox ────────────────
        var quoteNumber = quote.QuoteNumber ?? quote.Id.ToString();
        var pdfBytes = new QuotePdfDocument(quote, companyName, compiled.Sections).GeneratePdf();
        var termsUrl = $"{request.PublicBaseUrl.TrimEnd('/')}/api/v1/public/terms/{accessToken}";

        var message = new EmailMessage(
            To: request.RecipientEmail,
            Subject: $"Quote {quoteNumber} from {companyName}",
            HtmlBody: BuildHtmlBody(quote, companyName, quoteNumber, request.Message, compiled.Sections, termsUrl),
            Attachments:
            [
                new EmailAttachment(
                    $"Quote-{quoteNumber}.pdf",
                    "application/pdf",
                    pdfBytes)
            ]);

        var operationKey = $"quote-email:{quote.Id}:{request.RecipientEmail}";
        await outbox.EnqueueEmailAsync(
            operationKey,
            message,
            entityType: "Quote",
            entityId: quote.Id,
            ct: ct);

        // ── Status flip — same logic the plain /send endpoint uses ────────
        if (quote.Status == QuoteStatus.Draft)
        {
            await mediator.Send(new SendQuoteCommand(quote.Id), ct);
        }
        else
        {
            // Re-send of an already-Sent quote: refresh SentDate only
            // (SendQuoteCommand rejects non-Draft quotes by design).
            quote.SentDate = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// 32 random bytes as URL-safe base64 (43 chars, fits the 64-char column).
    /// Public + static so the token contract is directly unit-testable.
    /// </summary>
    public static string GenerateAccessToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string BuildHtmlBody(
        Quote quote,
        string companyName,
        string quoteNumber,
        string? personalMessage,
        IReadOnlyList<CompiledTermsSection> sections,
        string termsUrl)
    {
        var customerName = WebUtility.HtmlEncode(quote.Customer.CompanyName ?? quote.Customer.Name);
        var sb = new StringBuilder();

        sb.Append($"<h2>Quote {WebUtility.HtmlEncode(quoteNumber)}</h2>");
        sb.Append($"<p>Dear {customerName},</p>");
        sb.Append("<p>Thank you for the opportunity to quote. Please find your quote attached as a PDF; a summary follows below.</p>");

        if (!string.IsNullOrWhiteSpace(personalMessage))
            sb.Append($"<p style=\"border-left:3px solid #1565c0;padding-left:12px;\">{WebUtility.HtmlEncode(personalMessage.Trim()).Replace("\n", "<br/>")}</p>");

        // Quote summary table
        sb.Append("<table style=\"border-collapse:collapse;width:100%;max-width:640px;\" cellpadding=\"6\">");
        sb.Append("<tr style=\"background:#1565c0;color:#ffffff;text-align:left;\">")
          .Append("<th>Description</th><th style=\"text-align:right;\">Qty</th>")
          .Append("<th style=\"text-align:right;\">Unit Price</th><th style=\"text-align:right;\">Total</th></tr>");
        foreach (var line in quote.Lines.OrderBy(l => l.LineNumber))
        {
            sb.Append("<tr style=\"border-bottom:1px solid #e0e0e0;\">")
              .Append($"<td>{WebUtility.HtmlEncode(line.Description)}</td>")
              .Append($"<td style=\"text-align:right;\">{line.Quantity:0.####}</td>")
              .Append($"<td style=\"text-align:right;\">{line.UnitPrice:C}</td>")
              .Append($"<td style=\"text-align:right;\">{line.LineTotal:C}</td></tr>");
        }
        sb.Append($"<tr><td colspan=\"3\" style=\"text-align:right;\"><strong>Total:</strong></td>")
          .Append($"<td style=\"text-align:right;\"><strong>{quote.Total:C}</strong></td></tr>");
        sb.Append("</table>");

        if (quote.ExpirationDate.HasValue)
            sb.Append($"<p>This quote is valid until <strong>{quote.ExpirationDate:MM/dd/yyyy}</strong>.</p>");

        // Truncated terms sections + full-terms link
        if (sections.Count > 0)
        {
            sb.Append("<h3 style=\"margin-top:24px;\">Terms &amp; Conditions</h3>");
            foreach (var section in sections)
            {
                sb.Append($"<p style=\"margin:8px 0;\"><strong>{WebUtility.HtmlEncode(section.Title)}</strong><br/>")
                  .Append($"{WebUtility.HtmlEncode(section.Blurb)}</p>");
            }
            sb.Append($"<p><a href=\"{termsUrl}\">View full terms</a></p>");
        }

        sb.Append($"<p>— {WebUtility.HtmlEncode(companyName)}</p>");
        return sb.ToString();
    }
}
