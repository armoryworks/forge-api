using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PriceLists;

public record UpdatePriceListEntryCommand(
    int Id,
    decimal UnitPrice,
    int MinQuantity,
    string Currency,
    string? Notes) : IRequest<PriceListEntryResponseModel>;

public class UpdatePriceListEntryValidator : AbstractValidator<UpdatePriceListEntryCommand>
{
    public UpdatePriceListEntryValidator()
    {
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinQuantity).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class UpdatePriceListEntryHandler(IPriceListRepository repo)
    : IRequestHandler<UpdatePriceListEntryCommand, PriceListEntryResponseModel>
{
    public async Task<PriceListEntryResponseModel> Handle(
        UpdatePriceListEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = await repo.FindEntryWithPartAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Price list entry {request.Id} not found");

        entry.UnitPrice = request.UnitPrice;
        entry.MinQuantity = request.MinQuantity;
        entry.Currency = request.Currency;
        entry.Notes = request.Notes;

        await repo.SaveChangesAsync(cancellationToken);

        return new PriceListEntryResponseModel(
            entry.Id, entry.PriceListId, entry.PartId,
            entry.Part.PartNumber, entry.Part.Name,
            entry.UnitPrice, entry.MinQuantity,
            entry.Currency, entry.Notes,
            entry.CreatedAt, entry.UpdatedAt);
    }
}
