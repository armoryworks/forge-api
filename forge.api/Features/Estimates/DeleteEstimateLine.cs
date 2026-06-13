using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Estimates;

// SALES-LINE-CRUD: remove an estimate line (hard delete via cascade — QuoteLine
// has no soft-delete column). Unlike quotes/SOs, an estimate may legitimately
// drop to zero lines: it can still carry a bare lump-sum EstimatedAmount. When
// the last line is removed, EstimatedAmount falls back to 0 and can be set
// directly via UpdateEstimate.
public record DeleteEstimateLineCommand(int EstimateId, int LineId)
    : IRequest<EstimateDetailResponseModel>;

public class DeleteEstimateLineValidator : AbstractValidator<DeleteEstimateLineCommand>
{
    public DeleteEstimateLineValidator()
    {
        RuleFor(x => x.EstimateId).GreaterThan(0);
        RuleFor(x => x.LineId).GreaterThan(0);
    }
}

public class DeleteEstimateLineHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<DeleteEstimateLineCommand, EstimateDetailResponseModel>
{
    public async Task<EstimateDetailResponseModel> Handle(DeleteEstimateLineCommand request, CancellationToken ct)
    {
        var estimate = await db.Quotes
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == request.EstimateId && q.Type == QuoteType.Estimate && q.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Estimate {request.EstimateId} not found.");

        if (estimate.ConvertedAt != null)
            throw new InvalidOperationException("A converted estimate cannot be edited.");

        var line = estimate.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new KeyNotFoundException($"Estimate line {request.LineId} not found.");

        estimate.Lines.Remove(line);
        estimate.EstimatedAmount = estimate.Lines.Sum(l => l.Quantity * l.UnitPrice);

        db.LogActivityAt(
            "line-removed",
            $"Removed estimate line {line.LineNumber}: {line.Description}",
            ("Quote", estimate.Id));

        await db.SaveChangesAsync(ct);

        return await mediator.Send(new GetEstimateQuery(request.EstimateId), ct);
    }
}
