using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.TariffRates;

/// <summary>
/// Bought-parts effort PR4 — admin updates an existing TariffRate row.
/// SCD-2 supersession is the admin's responsibility: to retire an old
/// rate, admin sets <c>EffectiveTo</c> here and inserts a new row via
/// <see cref="CreateTariffRateCommand"/>. Keeps the audit trail explicit.
/// </summary>
public record UpdateTariffRateCommand(int Id, UpdateTariffRateRequestModel Body)
    : IRequest<TariffRateResponseModel>;

public class UpdateTariffRateValidator : AbstractValidator<UpdateTariffRateCommand>
{
    public UpdateTariffRateValidator()
    {
        RuleFor(x => x.Body.RatePct).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(1000m);
        RuleFor(x => x.Body.Source).MaximumLength(200).When(x => !string.IsNullOrEmpty(x.Body.Source));
    }
}

public class UpdateTariffRateHandler(AppDbContext db)
    : IRequestHandler<UpdateTariffRateCommand, TariffRateResponseModel>
{
    public async Task<TariffRateResponseModel> Handle(UpdateTariffRateCommand request, CancellationToken ct)
    {
        var entity = await db.TariffRates.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"TariffRate {request.Id} not found");
        entity.RatePct = request.Body.RatePct;
        entity.EffectiveFrom = request.Body.EffectiveFrom;
        entity.EffectiveTo = request.Body.EffectiveTo;
        entity.Source = request.Body.Source;
        await db.SaveChangesAsync(ct);
        return new TariffRateResponseModel(
            entity.Id, entity.HtsCode, entity.CountryOfOrigin, entity.RatePct,
            entity.EffectiveFrom, entity.EffectiveTo, entity.Source, entity.CreatedAt, entity.UpdatedAt);
    }
}
