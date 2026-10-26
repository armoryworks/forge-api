using System.Globalization;
using System.Net;
using System.Text;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Purchasing;

public record SendRfqToVendorsCommand(int RfqId, List<int> VendorIds) : IRequest<SendRfqToVendorsResponseModel>;

public class SendRfqToVendorsValidator : AbstractValidator<SendRfqToVendorsCommand>
{
    public SendRfqToVendorsValidator()
    {
        RuleFor(x => x.RfqId).GreaterThan(0);
        RuleFor(x => x.VendorIds).NotEmpty().WithMessage("At least one vendor is required");
    }
}

public class SendRfqToVendorsHandler(
    AppDbContext db,
    IRfqService rfqService,
    IIntegrationOutboxService outbox,
    IMediator mediator)
    : IRequestHandler<SendRfqToVendorsCommand, SendRfqToVendorsResponseModel>
{
    public async Task<SendRfqToVendorsResponseModel> Handle(SendRfqToVendorsCommand request, CancellationToken ct)
    {
        var rfq = await db.RequestForQuotes
            .AsNoTracking()
            .Include(r => r.Part)
            .Include(r => r.VendorResponses)
            .FirstOrDefaultAsync(r => r.Id == request.RfqId, ct)
            ?? throw new KeyNotFoundException($"RFQ {request.RfqId} not found");

        if (rfq.Status is RfqStatus.Awarded or RfqStatus.Cancelled or RfqStatus.Expired)
            throw new InvalidOperationException($"Cannot send an RFQ in status {rfq.Status}.");

        var vendorIds = request.VendorIds.Distinct().ToList();
        var vendors = await db.Vendors
            .AsNoTracking()
            .Where(v => vendorIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        foreach (var id in vendorIds.Where(id => !vendors.ContainsKey(id)))
            throw new KeyNotFoundException($"Vendor {id} not found");

        var invited = rfq.VendorResponses.Select(r => r.VendorId).ToHashSet();
        var alreadyInvited = vendorIds.Where(invited.Contains).ToList();
        var newVendorIds = vendorIds.Where(id => !invited.Contains(id)).ToList();
        var reachable = newVendorIds.Where(id => !string.IsNullOrWhiteSpace(vendors[id].Email)).ToList();
        var withoutEmail = newVendorIds
            .Where(id => string.IsNullOrWhiteSpace(vendors[id].Email))
            .Select(id => new RfqVendorWithoutEmailModel(id, vendors[id].CompanyName))
            .ToList();

        if (reachable.Count == 0)
            return new SendRfqToVendorsResponseModel(rfq.Id, [], withoutEmail, alreadyInvited);

        var companyName = await CompanyIdentity.GetCompanyNameAsync(db, ct)
            ?? throw new InvalidOperationException(SendPurchaseOrderEmailHandler.MissingCompanyNameMessage);

        var attachments = new Dictionary<int, byte[]>();
        foreach (var vendorId in reachable)
            attachments[vendorId] = await mediator.Send(new GetRfqPdfQuery(rfq.Id, vendorId), ct);

        db.LogActivityAt(
            "sent",
            $"Sent to {string.Join(", ", reachable.Select(id => vendors[id].CompanyName))}",
            ("RequestForQuote", rfq.Id));
        await rfqService.SendToVendorsAsync(rfq.Id, reachable, ct);

        foreach (var vendorId in reachable)
        {
            var vendor = vendors[vendorId];
            var to = vendor.Email!.Trim();
            var message = new EmailMessage(
                To: to,
                Subject: $"Request for Quote {rfq.RfqNumber} from {companyName}",
                HtmlBody: BuildHtmlBody(rfq, vendor, companyName),
                Attachments:
                [
                    new EmailAttachment($"RFQ-{rfq.RfqNumber}.pdf", "application/pdf", attachments[vendorId]),
                ]);
            await outbox.EnqueueEmailAsync(
                $"rfq-email:{rfq.Id}:{vendorId}:{to}", message, entityType: "RequestForQuote", entityId: rfq.Id, ct: ct);
        }

        return new SendRfqToVendorsResponseModel(rfq.Id, reachable, withoutEmail, alreadyInvited);
    }

    private static string BuildHtmlBody(RequestForQuote rfq, Vendor vendor, string companyName)
    {
        var sb = new StringBuilder();
        sb.Append($"<h2>Request for Quote {WebUtility.HtmlEncode(rfq.RfqNumber)}</h2>");
        sb.Append($"<p>Dear {WebUtility.HtmlEncode(vendor.ContactName ?? vendor.CompanyName)},</p>");
        sb.Append($"<p>Please quote {rfq.Quantity.ToString("0.####", CultureInfo.InvariantCulture)} of {WebUtility.HtmlEncode(rfq.Part.GetDisplayName())}. The full request is attached as a PDF.</p>");
        if (rfq.ResponseDeadline.HasValue)
            sb.Append($"<p>Please respond by <strong>{rfq.ResponseDeadline.Value.UtcDateTime.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)}</strong>.</p>");
        sb.Append($"<p>— {WebUtility.HtmlEncode(companyName)}</p>");
        return sb.ToString();
    }
}
