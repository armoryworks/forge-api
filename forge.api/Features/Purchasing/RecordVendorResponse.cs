using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Purchasing;

public record RecordVendorResponseCommand(
    int RfqId,
    int VendorId,
    decimal? UnitPrice,
    int? LeadTimeDays,
    decimal? MinimumOrderQuantity,
    decimal? ToolingCost,
    DateTimeOffset? QuoteValidUntil,
    string? Notes) : IRequest;

public class RecordVendorResponseValidator : AbstractValidator<RecordVendorResponseCommand>
{
    public RecordVendorResponseValidator()
    {
        RuleFor(x => x.RfqId).GreaterThan(0);
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).LessThan(MaxAmount);
        RuleFor(x => x.LeadTimeDays).InclusiveBetween(0, 3650);
        RuleFor(x => x.MinimumOrderQuantity).GreaterThan(0).LessThan(MaxAmount);
        RuleFor(x => x.ToolingCost).GreaterThanOrEqualTo(0).LessThan(MaxAmount);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }

    private const decimal MaxAmount = 100_000_000_000_000m;
}

public class RecordVendorResponseHandler(AppDbContext db, IClock clock)
    : IRequestHandler<RecordVendorResponseCommand>
{
    public async Task Handle(RecordVendorResponseCommand request, CancellationToken cancellationToken)
    {
        var vendorResponse = await db.RfqVendorResponses
            .Include(r => r.Rfq)
            .FirstOrDefaultAsync(r => r.RfqId == request.RfqId && r.VendorId == request.VendorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor response for vendor {request.VendorId} on RFQ {request.RfqId} not found");

        if (vendorResponse.ResponseStatus != RfqResponseStatus.Pending)
            throw new InvalidOperationException("Response has already been recorded");

        vendorResponse.UnitPrice = request.UnitPrice;
        vendorResponse.LeadTimeDays = request.LeadTimeDays;
        vendorResponse.MinimumOrderQuantity = request.MinimumOrderQuantity;
        vendorResponse.ToolingCost = request.ToolingCost;
        vendorResponse.QuoteValidUntil = request.QuoteValidUntil;
        vendorResponse.Notes = request.Notes;
        vendorResponse.ResponseStatus = RfqResponseStatus.Received;
        vendorResponse.RespondedAt = clock.UtcNow;

        // Update RFQ status to Receiving if not already
        if (vendorResponse.Rfq.Status == RfqStatus.Sent)
            vendorResponse.Rfq.Status = RfqStatus.Receiving;

        await db.SaveChangesAsync(cancellationToken);
    }
}
