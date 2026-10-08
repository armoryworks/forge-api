using System.Globalization;
using System.Net;
using System.Text;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public record SendPurchaseOrderEmailCommand(int Id, string? To, string? Cc, string? Message) : IRequest;

public class SendPurchaseOrderEmailValidator : AbstractValidator<SendPurchaseOrderEmailCommand>
{
    public SendPurchaseOrderEmailValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.To!.Trim())
            .EmailAddress()
            .MaximumLength(320)
            .When(x => !string.IsNullOrWhiteSpace(x.To))
            .OverridePropertyName("to");
        RuleForEach(x => SendPurchaseOrderEmailHandler.SplitAddresses(x.Cc))
            .EmailAddress()
            .MaximumLength(320)
            .OverridePropertyName("cc");
        RuleFor(x => x.Message).MaximumLength(4000);
    }
}

public class SendPurchaseOrderEmailHandler(
    AppDbContext db,
    IIntegrationOutboxService outbox,
    IClock clock,
    IMediator mediator) : IRequestHandler<SendPurchaseOrderEmailCommand>
{
    public const string MissingCompanyNameMessage =
        "Set your company name in Admin > Company before sending documents to vendors.";

    public async Task Handle(SendPurchaseOrderEmailCommand request, CancellationToken ct)
    {
        var po = await db.PurchaseOrders
            .Include(p => p.Vendor)
            .FirstOrDefaultAsync(p => p.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        if (po.Status == PurchaseOrderStatus.Cancelled)
            throw new InvalidOperationException("Cancelled purchase orders cannot be emailed.");

        var to = string.IsNullOrWhiteSpace(request.To) ? po.Vendor.Email?.Trim() : request.To.Trim();
        if (string.IsNullOrWhiteSpace(to))
            throw new ValidationException(new[]
            {
                new ValidationFailure("to", $"{po.Vendor.CompanyName} has no email address. Enter a recipient."),
            });

        var companyName = await CompanyIdentity.GetCompanyNameAsync(db, ct)
            ?? throw new InvalidOperationException(MissingCompanyNameMessage);

        var cc = SplitAddresses(request.Cc);
        var pdfBytes = await mediator.Send(new GetPurchaseOrderPdfQuery(po.Id), ct);

        db.LogActivityAt(
            "emailed",
            cc.Count == 0 ? $"Emailed to {to}" : $"Emailed to {to}, cc {string.Join(", ", cc)}",
            ("PurchaseOrder", po.Id));
        await db.SaveChangesAsync(ct);

        var message = new EmailMessage(
            To: to,
            Subject: $"Purchase Order {po.PONumber} from {companyName}",
            HtmlBody: BuildHtmlBody(po, companyName, request.Message),
            Attachments:
            [
                new EmailAttachment($"PurchaseOrder-{po.PONumber}.pdf", "application/pdf", pdfBytes),
            ],
            Cc: cc.Count == 0 ? null : cc);

        var operationKey = $"po-email:{po.Id}:{to}:{clock.UtcNow.ToUnixTimeSeconds()}";
        await outbox.EnqueueEmailAsync(operationKey, message, entityType: "PurchaseOrder", entityId: po.Id, ct: ct);
    }

    public static List<string> SplitAddresses(string? addresses) =>
        string.IsNullOrWhiteSpace(addresses)
            ? []
            : addresses
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string BuildHtmlBody(PurchaseOrder po, string companyName, string? personalMessage)
    {
        var vendorName = WebUtility.HtmlEncode(po.Vendor.ContactName ?? po.Vendor.CompanyName);
        var sb = new StringBuilder();

        sb.Append($"<h2>Purchase Order {WebUtility.HtmlEncode(po.PONumber)}</h2>");
        sb.Append($"<p>Dear {vendorName},</p>");
        sb.Append("<p>Please find our purchase order attached as a PDF.</p>");

        if (!string.IsNullOrWhiteSpace(personalMessage))
            sb.Append($"<p style=\"border-left:3px solid #1565c0;padding-left:12px;\">{WebUtility.HtmlEncode(personalMessage.Trim()).Replace("\n", "<br/>")}</p>");

        if (po.ExpectedDeliveryDate.HasValue)
            sb.Append($"<p>Requested delivery: <strong>{po.ExpectedDeliveryDate.Value.UtcDateTime.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)}</strong>.</p>");

        sb.Append("<p>Please confirm receipt and the expected ship date.</p>");
        sb.Append($"<p>— {WebUtility.HtmlEncode(companyName)}</p>");
        return sb.ToString();
    }
}
