using FluentValidation;
using MediatR;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PriceLists;

public record CreatePriceListCommand(
    string Name,
    string? Description,
    int? CustomerId,
    bool IsDefault,
    bool IsActive,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    List<CreatePriceListEntryModel>? Entries) : IRequest<PriceListListItemModel>;

public class CreatePriceListValidator : AbstractValidator<CreatePriceListCommand>
{
    public CreatePriceListValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x).Must(c => c.EffectiveFrom == null || c.EffectiveTo == null || c.EffectiveTo > c.EffectiveFrom)
            .WithMessage("Effective To must be after Effective From");
        // Entries are optional — the Customer Pricing tab UI creates an empty
        // list and adds entries one at a time. Seed/import flows can still
        // pass an initial batch.
        When(x => x.Entries != null && x.Entries.Count > 0, () =>
        {
            RuleForEach(x => x.Entries!).ChildRules(entry =>
            {
                entry.RuleFor(e => e.PartId).GreaterThan(0);
                entry.RuleFor(e => e.UnitPrice).GreaterThanOrEqualTo(0);
                entry.RuleFor(e => e.MinQuantity).GreaterThan(0);
            });
        });
    }
}

public class CreatePriceListHandler(IPriceListRepository repo)
    : IRequestHandler<CreatePriceListCommand, PriceListListItemModel>
{
    public async Task<PriceListListItemModel> Handle(CreatePriceListCommand request, CancellationToken cancellationToken)
    {
        var priceList = new PriceList
        {
            Name = request.Name,
            Description = request.Description,
            CustomerId = request.CustomerId,
            IsDefault = request.IsDefault,
            IsActive = request.IsActive,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
        };

        if (request.Entries != null)
        {
            foreach (var entry in request.Entries)
            {
                priceList.Entries.Add(new PriceListEntry
                {
                    PartId = entry.PartId,
                    UnitPrice = entry.UnitPrice,
                    MinQuantity = entry.MinQuantity,
                });
            }
        }

        // Mirror VendorPart's IsPreferred logic: only one default per scope
        // (per-customer, or system-wide when CustomerId is null). Unset the
        // default flag on every other list in the same scope before save so
        // the database never holds two defaults at once.
        if (request.IsDefault)
        {
            await repo.UnsetDefaultForScopeAsync(request.CustomerId, excludePriceListId: null, cancellationToken);
        }

        await repo.AddAsync(priceList, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        return new PriceListListItemModel(
            priceList.Id, priceList.Name, priceList.Description,
            priceList.CustomerId, null, priceList.IsDefault, priceList.IsActive,
            priceList.Entries.Count, priceList.CreatedAt);
    }
}
