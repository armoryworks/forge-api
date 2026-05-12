using FluentValidation;
using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TariffRates;

public record CreateTariffRateCommand(CreateTariffRateRequestModel Body)
    : IRequest<TariffRateResponseModel>;

public class CreateTariffRateValidator : AbstractValidator<CreateTariffRateCommand>
{
    public CreateTariffRateValidator()
    {
        RuleFor(x => x.Body.HtsCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Body.CountryOfOrigin).NotEmpty().Length(2)
            .WithMessage("CountryOfOrigin must be a 2-letter ISO-3166 alpha-2 code.");
        RuleFor(x => x.Body.RatePct).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(1000m);
        RuleFor(x => x.Body.Source).MaximumLength(200).When(x => !string.IsNullOrEmpty(x.Body.Source));
    }
}

public class CreateTariffRateHandler(AppDbContext db)
    : IRequestHandler<CreateTariffRateCommand, TariffRateResponseModel>
{
    public async Task<TariffRateResponseModel> Handle(CreateTariffRateCommand request, CancellationToken ct)
    {
        var entity = new TariffRate
        {
            HtsCode = request.Body.HtsCode.Trim(),
            CountryOfOrigin = request.Body.CountryOfOrigin.Trim().ToUpperInvariant(),
            RatePct = request.Body.RatePct,
            EffectiveFrom = request.Body.EffectiveFrom,
            EffectiveTo = request.Body.EffectiveTo,
            Source = request.Body.Source,
        };
        db.TariffRates.Add(entity);
        await db.SaveChangesAsync(ct);
        return new TariffRateResponseModel(
            entity.Id, entity.HtsCode, entity.CountryOfOrigin, entity.RatePct,
            entity.EffectiveFrom, entity.EffectiveTo, entity.Source, entity.CreatedAt, entity.UpdatedAt);
    }
}
