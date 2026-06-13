using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Estimates;

// SALES-LINE-CRUD: estimates are Quote rows (Type=Estimate) that already carry a
// Lines collection — the estimate flow just never populated it. This adds line
// items so an estimate can mix pre-existing catalog parts (PartId set) with
// lump-sum/ad-hoc lines for unknowns (PartId null, free-text + amount).
// EstimatedAmount is kept in sync as the sum of line totals so list views that
// read it stay correct.
public record AddEstimateLineCommand(int EstimateId, CreateQuoteLineModel Data)
    : IRequest<EstimateDetailResponseModel>;

public class AddEstimateLineValidator : AbstractValidator<AddEstimateLineCommand>
{
    public AddEstimateLineValidator()
    {
        RuleFor(x => x.EstimateId).GreaterThan(0);
        RuleFor(x => x.Data.Description).NotEmpty();
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class AddEstimateLineHandler(AppDbContext db, IPartRepository partRepo, IMediator mediator)
    : IRequestHandler<AddEstimateLineCommand, EstimateDetailResponseModel>
{
    public async Task<EstimateDetailResponseModel> Handle(AddEstimateLineCommand request, CancellationToken ct)
    {
        var estimate = await db.Quotes
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.EstimateId && q.Type == QuoteType.Estimate && q.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Estimate {request.EstimateId} not found.");

        if (estimate.ConvertedAt != null)
            throw new InvalidOperationException("A converted estimate cannot be edited.");

        if (request.Data.PartId is int partId && partId > 0)
        {
            var part = await partRepo.FindAsync(partId, ct);
            ActiveCheck.EnsureActive(part, "Part", "partId", partId);
        }

        var nextLineNumber = estimate.Lines.Count == 0 ? 1 : estimate.Lines.Max(l => l.LineNumber) + 1;

        estimate.Lines.Add(new QuoteLine
        {
            PartId = request.Data.PartId,
            Description = request.Data.Description,
            Quantity = request.Data.Quantity,
            UnitPrice = request.Data.UnitPrice,
            LineNumber = nextLineNumber,
            Notes = request.Data.Notes,
        });
        estimate.EstimatedAmount = estimate.Lines.Sum(l => l.Quantity * l.UnitPrice);

        db.LogActivityAt(
            "line-added",
            $"Added estimate line {nextLineNumber}: {request.Data.Description} ({request.Data.Quantity} × {request.Data.UnitPrice})",
            ("Quote", estimate.Id));

        await db.SaveChangesAsync(ct);

        return await mediator.Send(new GetEstimateQuery(request.EstimateId), ct);
    }
}
