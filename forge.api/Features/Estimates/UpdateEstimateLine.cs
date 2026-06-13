using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Estimates;

// SALES-LINE-CRUD: edit a single estimate line. Reuses the shared
// UpdateOrderLineRequestModel (description/qty/price/notes); the part link is
// fixed at add time. EstimatedAmount is re-summed from the lines.
public record UpdateEstimateLineCommand(int EstimateId, int LineId, UpdateOrderLineRequestModel Data)
    : IRequest<EstimateDetailResponseModel>;

public class UpdateEstimateLineValidator : AbstractValidator<UpdateEstimateLineCommand>
{
    public UpdateEstimateLineValidator()
    {
        RuleFor(x => x.EstimateId).GreaterThan(0);
        RuleFor(x => x.LineId).GreaterThan(0);
        RuleFor(x => x.Data.Description).NotEmpty();
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class UpdateEstimateLineHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<UpdateEstimateLineCommand, EstimateDetailResponseModel>
{
    public async Task<EstimateDetailResponseModel> Handle(UpdateEstimateLineCommand request, CancellationToken ct)
    {
        var estimate = await db.Quotes
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.EstimateId && q.Type == QuoteType.Estimate && q.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Estimate {request.EstimateId} not found.");

        if (estimate.ConvertedAt != null)
            throw new InvalidOperationException("A converted estimate cannot be edited.");

        var line = estimate.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new KeyNotFoundException($"Estimate line {request.LineId} not found.");

        line.Description = request.Data.Description;
        line.Quantity = request.Data.Quantity;
        line.UnitPrice = request.Data.UnitPrice;
        line.Notes = request.Data.Notes;
        estimate.EstimatedAmount = estimate.Lines.Sum(l => l.Quantity * l.UnitPrice);

        db.LogActivityAt(
            "line-updated",
            $"Updated estimate line {line.LineNumber}: {line.Description}",
            ("Quote", estimate.Id));

        await db.SaveChangesAsync(ct);

        return await mediator.Send(new GetEstimateQuery(request.EstimateId), ct);
    }
}
