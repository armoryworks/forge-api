using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Estimates;

public record CreateEstimateCommand(
    int CustomerId,
    string Title,
    string? Description,
    decimal EstimatedAmount,
    DateTimeOffset? ValidUntil,
    string? Notes,
    int? AssignedToId) : IRequest<EstimateListItemModel>;

public class CreateEstimateValidator : AbstractValidator<CreateEstimateCommand>
{
    public CreateEstimateValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.EstimatedAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
    }
}

public class CreateEstimateHandler(AppDbContext db) : IRequestHandler<CreateEstimateCommand, EstimateListItemModel>
{
    public async Task<EstimateListItemModel> Handle(CreateEstimateCommand request, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([request.CustomerId], ct)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found.");

        var estimate = new Quote
        {
            Type = QuoteType.Estimate,
            CustomerId = request.CustomerId,
            Title = request.Title,
            Description = request.Description,
            EstimatedAmount = request.EstimatedAmount,
            ExpirationDate = request.ValidUntil,
            Notes = request.Notes,
            AssignedToId = request.AssignedToId,
        };

        db.Quotes.Add(estimate);
        await db.SaveChangesAsync(ct);

        return new EstimateListItemModel(
            estimate.Id,
            estimate.CustomerId,
            customer.Name,
            estimate.Title!,
            estimate.EstimatedAmount ?? 0,
            estimate.Status.ToString(),
            estimate.ExpirationDate,
            null,
            null,
            estimate.CreatedAt);
    }
}
