using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Purchasing;

public record CreateRfqCommand(
    int PartId,
    decimal Quantity,
    DateTimeOffset RequiredDate,
    string? Description,
    string? SpecialInstructions,
    DateTimeOffset? ResponseDeadline) : IRequest<RfqResponseModel>;

public class CreateRfqValidator : AbstractValidator<CreateRfqCommand>
{
    public CreateRfqValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.RequiredDate).NotEmpty();
    }
}

public class CreateRfqHandler(AppDbContext db, IRfqService rfqService)
    : IRequestHandler<CreateRfqCommand, RfqResponseModel>
{
    public async Task<RfqResponseModel> Handle(CreateRfqCommand request, CancellationToken cancellationToken)
    {
        var part = await db.Parts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var rfqNumber = await rfqService.GenerateRfqNumberAsync(cancellationToken);

        var rfq = new RequestForQuote
        {
            RfqNumber = rfqNumber,
            PartId = request.PartId,
            Quantity = request.Quantity,
            RequiredDate = request.RequiredDate,
            Description = request.Description,
            SpecialInstructions = request.SpecialInstructions,
            ResponseDeadline = request.ResponseDeadline,
        };

        db.RequestForQuotes.Add(rfq);
        await db.SaveChangesAsync(cancellationToken);

        return new RfqResponseModel(
            rfq.Id,
            rfq.RfqNumber,
            rfq.PartId,
            part.PartNumber,
            part.Description ?? part.Name,
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
            0,
            0,
            rfq.CreatedAt);
    }
}
