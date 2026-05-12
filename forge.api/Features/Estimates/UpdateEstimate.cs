using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Estimates;

public record UpdateEstimateCommand(
    int Id,
    string? Title,
    string? Description,
    decimal? EstimatedAmount,
    QuoteStatus? Status,
    DateTimeOffset? ValidUntil,
    string? Notes,
    int? AssignedToId) : IRequest;

public class UpdateEstimateValidator : AbstractValidator<UpdateEstimateCommand>
{
    public UpdateEstimateValidator()
    {
        RuleFor(x => x.Title).MaximumLength(300).When(x => x.Title is not null);
        RuleFor(x => x.EstimatedAmount).GreaterThanOrEqualTo(0).When(x => x.EstimatedAmount.HasValue);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class UpdateEstimateHandler(AppDbContext db) : IRequestHandler<UpdateEstimateCommand>
{
    public async Task Handle(UpdateEstimateCommand request, CancellationToken ct)
    {
        var estimate = await db.Quotes
            .FirstOrDefaultAsync(q => q.Id == request.Id && q.Type == QuoteType.Estimate, ct)
            ?? throw new KeyNotFoundException($"Estimate {request.Id} not found.");

        if (estimate.DeletedAt != null)
            throw new KeyNotFoundException($"Estimate {request.Id} not found.");

        if (request.Title is not null) estimate.Title = request.Title;
        if (request.Description is not null) estimate.Description = request.Description;
        if (request.EstimatedAmount.HasValue) estimate.EstimatedAmount = request.EstimatedAmount.Value;
        if (request.Status.HasValue) estimate.Status = request.Status.Value;
        if (request.ValidUntil.HasValue) estimate.ExpirationDate = request.ValidUntil;
        if (request.Notes is not null) estimate.Notes = request.Notes;
        estimate.AssignedToId = request.AssignedToId;

        await db.SaveChangesAsync(ct);
    }
}
