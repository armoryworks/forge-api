using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PriceLists;

public record UpdatePriceListCommand(
    int Id,
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo) : IRequest<PriceListListItemModel>;

public class UpdatePriceListValidator : AbstractValidator<UpdatePriceListCommand>
{
    public UpdatePriceListValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x).Must(c => c.EffectiveFrom == null || c.EffectiveTo == null || c.EffectiveTo > c.EffectiveFrom)
            .WithMessage("Effective To must be after Effective From");
    }
}

public class UpdatePriceListHandler(IPriceListRepository repo)
    : IRequestHandler<UpdatePriceListCommand, PriceListListItemModel>
{
    public async Task<PriceListListItemModel> Handle(UpdatePriceListCommand request, CancellationToken cancellationToken)
    {
        var priceList = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Price list {request.Id} not found");

        priceList.Name = request.Name;
        priceList.Description = request.Description;
        priceList.IsActive = request.IsActive;
        priceList.EffectiveFrom = request.EffectiveFrom;
        priceList.EffectiveTo = request.EffectiveTo;

        // IsDefault uniqueness — same logic as Create. Only flip other lists
        // off when this list is becoming the default. Pass excludePriceListId
        // so we don't accidentally clear the flag we're about to set.
        if (request.IsDefault && !priceList.IsDefault)
        {
            await repo.UnsetDefaultForScopeAsync(priceList.CustomerId, excludePriceListId: priceList.Id, cancellationToken);
        }
        priceList.IsDefault = request.IsDefault;

        await repo.SaveChangesAsync(cancellationToken);

        return new PriceListListItemModel(
            priceList.Id, priceList.Name, priceList.Description,
            priceList.CustomerId, priceList.Customer?.Name,
            priceList.IsDefault, priceList.IsActive,
            priceList.Entries?.Count ?? 0, priceList.CreatedAt);
    }
}
