using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Purchasing;

public record UpdateRfqCommand(
    int Id,
    int PartId,
    decimal Quantity,
    DateTimeOffset RequiredDate,
    string? Description,
    string? SpecialInstructions,
    DateTimeOffset? ResponseDeadline,
    string? Notes) : IRequest;

public class UpdateRfqValidator : AbstractValidator<UpdateRfqCommand>
{
    public UpdateRfqValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.RequiredDate).NotEmpty();
    }
}

public class UpdateRfqHandler(AppDbContext db)
    : IRequestHandler<UpdateRfqCommand>
{
    public async Task Handle(UpdateRfqCommand request, CancellationToken cancellationToken)
    {
        var rfq = await db.RequestForQuotes
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"RFQ {request.Id} not found");

        if (rfq.Status != RfqStatus.Draft)
            throw new InvalidOperationException("Can only edit RFQs in Draft status");

        var part = await db.Parts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        rfq.PartId = request.PartId;
        rfq.Quantity = request.Quantity;
        rfq.RequiredDate = request.RequiredDate;
        rfq.Description = request.Description;
        rfq.SpecialInstructions = request.SpecialInstructions;
        rfq.ResponseDeadline = request.ResponseDeadline;
        rfq.Notes = request.Notes;

        await db.SaveChangesAsync(cancellationToken);
    }
}
