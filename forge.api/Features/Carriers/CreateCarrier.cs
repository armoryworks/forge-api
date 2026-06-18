using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Carriers;

public record CreateCarrierCommand(
    string Name,
    string? Code,
    string? Scac,
    string IntegrationKind,
    string DeliveryUpdateMode,
    string? IntegrationServiceId,
    bool RequiresScanToShip,
    string? Notes) : IRequest<CarrierListItemModel>;

public class CreateCarrierValidator : AbstractValidator<CreateCarrierCommand>
{
    public CreateCarrierValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).MaximumLength(50);
        RuleFor(x => x.Scac).MaximumLength(10);
        RuleFor(x => x.IntegrationServiceId).MaximumLength(50);
        RuleFor(x => x.IntegrationKind)
            .Must(v => Enum.TryParse<CarrierIntegrationKind>(v, ignoreCase: true, out _))
            .WithMessage("IntegrationKind must be one of: Manual, Api");
        RuleFor(x => x.DeliveryUpdateMode)
            .Must(v => Enum.TryParse<CarrierDeliveryUpdateMode>(v, ignoreCase: true, out _))
            .WithMessage("DeliveryUpdateMode must be one of: Manual, Poll, Webhook");
    }
}

public class CreateCarrierHandler(AppDbContext db) : IRequestHandler<CreateCarrierCommand, CarrierListItemModel>
{
    public async Task<CarrierListItemModel> Handle(CreateCarrierCommand request, CancellationToken cancellationToken)
    {
        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        if (code is not null && await db.Carriers.AnyAsync(c => c.Code == code, cancellationToken))
            throw new InvalidOperationException($"A carrier with code '{code}' already exists.");

        var carrier = new Carrier
        {
            Name = request.Name.Trim(),
            Code = code,
            Scac = string.IsNullOrWhiteSpace(request.Scac) ? null : request.Scac.Trim().ToUpperInvariant(),
            IntegrationKind = Enum.Parse<CarrierIntegrationKind>(request.IntegrationKind, ignoreCase: true),
            DeliveryUpdateMode = Enum.Parse<CarrierDeliveryUpdateMode>(request.DeliveryUpdateMode, ignoreCase: true),
            IntegrationServiceId = string.IsNullOrWhiteSpace(request.IntegrationServiceId)
                ? null : request.IntegrationServiceId.Trim().ToLowerInvariant(),
            RequiresScanToShip = request.RequiresScanToShip,
            Notes = request.Notes,
        };

        db.Carriers.Add(carrier);
        await db.SaveChangesAsync(cancellationToken);

        db.LogActivityAt("created", $"Carrier {carrier.Name} created", ("Carrier", carrier.Id));
        await db.SaveChangesAsync(cancellationToken);

        return new CarrierListItemModel(
            carrier.Id, carrier.Name, carrier.Code, carrier.Scac,
            carrier.IntegrationKind.ToString(), carrier.DeliveryUpdateMode.ToString(),
            carrier.IntegrationServiceId, carrier.RequiresScanToShip, carrier.IsActive, carrier.SortOrder);
    }
}
