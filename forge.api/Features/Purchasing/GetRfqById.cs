using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Purchasing;

public record GetRfqByIdQuery(int Id) : IRequest<RfqDetailResponseModel>;

public record RfqDetailResponseModel(
    int Id,
    string RfqNumber,
    int PartId,
    string PartNumber,
    string PartDescription,
    decimal Quantity,
    DateTimeOffset RequiredDate,
    string Status,
    string? Description,
    string? SpecialInstructions,
    DateTimeOffset? ResponseDeadline,
    DateTimeOffset? SentAt,
    DateTimeOffset? AwardedAt,
    int? AwardedVendorResponseId,
    int? GeneratedPurchaseOrderId,
    string? Notes,
    DateTimeOffset CreatedAt,
    List<RfqVendorResponseModel> VendorResponses);

public class GetRfqByIdHandler(AppDbContext db)
    : IRequestHandler<GetRfqByIdQuery, RfqDetailResponseModel>
{
    public async Task<RfqDetailResponseModel> Handle(GetRfqByIdQuery request, CancellationToken cancellationToken)
    {
        var rfq = await db.RequestForQuotes
            .AsNoTracking()
            .Include(r => r.Part)
            .Include(r => r.VendorResponses)
                .ThenInclude(v => v.Vendor)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"RFQ {request.Id} not found");

        return new RfqDetailResponseModel(
            rfq.Id,
            rfq.RfqNumber,
            rfq.PartId,
            rfq.Part.PartNumber,
            rfq.Part.Description ?? rfq.Part.Name,
            rfq.Quantity,
            rfq.RequiredDate,
            rfq.Status.ToString(),
            rfq.Description,
            rfq.SpecialInstructions,
            rfq.ResponseDeadline,
            rfq.SentAt,
            rfq.AwardedAt,
            rfq.AwardedVendorResponseId,
            rfq.GeneratedPurchaseOrderId,
            rfq.Notes,
            rfq.CreatedAt,
            rfq.VendorResponses.Select(v => new RfqVendorResponseModel(
                v.Id,
                v.RfqId,
                v.VendorId,
                v.Vendor.CompanyName,
                v.ResponseStatus.ToString(),
                v.UnitPrice,
                v.LeadTimeDays,
                v.MinimumOrderQuantity,
                v.ToolingCost,
                v.QuoteValidUntil,
                v.Notes,
                v.InvitedAt,
                v.RespondedAt,
                v.IsAwarded,
                v.DeclineReason)).ToList());
    }
}
