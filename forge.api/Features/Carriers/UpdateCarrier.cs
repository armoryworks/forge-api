using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Carriers;

public record UpdateCarrierCommand(
    int Id,
    string? Name,
    string? Code,
    string? Scac,
    string? IntegrationKind,
    string? DeliveryUpdateMode,
    string? IntegrationServiceId,
    bool? RequiresScanToShip,
    bool? IsActive,
    int? SortOrder,
    string? Notes) : IRequest<CarrierListItemModel>;

public class UpdateCarrierValidator : AbstractValidator<UpdateCarrierCommand>
{
    public UpdateCarrierValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).When(x => x.Name is not null);
        RuleFor(x => x.Code).MaximumLength(50);
        RuleFor(x => x.Scac).MaximumLength(10);
        RuleFor(x => x.IntegrationServiceId).MaximumLength(50);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).When(x => x.SortOrder is not null);
        RuleFor(x => x.IntegrationKind)
            .Must(v => Enum.TryParse<CarrierIntegrationKind>(v, ignoreCase: true, out _))
            .When(x => x.IntegrationKind is not null)
            .WithMessage("IntegrationKind must be one of: Manual, Api");
        RuleFor(x => x.DeliveryUpdateMode)
            .Must(v => Enum.TryParse<CarrierDeliveryUpdateMode>(v, ignoreCase: true, out _))
            .When(x => x.DeliveryUpdateMode is not null)
            .WithMessage("DeliveryUpdateMode must be one of: Manual, Poll, Webhook");
    }
}

public class UpdateCarrierHandler(AppDbContext db) : IRequestHandler<UpdateCarrierCommand, CarrierListItemModel>
{
    public async Task<CarrierListItemModel> Handle(UpdateCarrierCommand request, CancellationToken cancellationToken)
    {
        var carrier = await db.Carriers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Carrier {request.Id} not found");

        var changedFields = new List<string>();

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name != carrier.Name) { carrier.Name = name; changedFields.Add("name"); }
        }

        if (request.Code is not null)
        {
            var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
            if (code != carrier.Code)
            {
                if (code is not null
                    && await db.Carriers.AnyAsync(c => c.Code == code && c.Id != carrier.Id, cancellationToken))
                    throw new InvalidOperationException($"A carrier with code '{code}' already exists.");
                carrier.Code = code;
                changedFields.Add("code");
            }
        }

        if (request.Scac is not null)
        {
            var scac = string.IsNullOrWhiteSpace(request.Scac) ? null : request.Scac.Trim().ToUpperInvariant();
            if (scac != carrier.Scac) { carrier.Scac = scac; changedFields.Add("scac"); }
        }

        if (request.IntegrationKind is not null)
        {
            var kind = Enum.Parse<CarrierIntegrationKind>(request.IntegrationKind, ignoreCase: true);
            if (kind != carrier.IntegrationKind) { carrier.IntegrationKind = kind; changedFields.Add("integrationKind"); }
        }

        if (request.IntegrationServiceId is not null)
        {
            var serviceId = string.IsNullOrWhiteSpace(request.IntegrationServiceId)
                ? null : request.IntegrationServiceId.Trim().ToLowerInvariant();
            if (serviceId != carrier.IntegrationServiceId)
            {
                carrier.IntegrationServiceId = serviceId;
                changedFields.Add("integrationServiceId");
            }
        }

        if (carrier.IntegrationKind == CarrierIntegrationKind.Api && string.IsNullOrWhiteSpace(carrier.IntegrationServiceId))
            throw new InvalidOperationException(
                "An integrated (API) carrier needs an integration service id (ups, fedex, usps or dhl).");

        if (request.DeliveryUpdateMode is not null)
        {
            var mode = Enum.Parse<CarrierDeliveryUpdateMode>(request.DeliveryUpdateMode, ignoreCase: true);
            if (mode != carrier.DeliveryUpdateMode) { carrier.DeliveryUpdateMode = mode; changedFields.Add("deliveryUpdateMode"); }
        }

        if (request.RequiresScanToShip is bool requiresScan && requiresScan != carrier.RequiresScanToShip)
        {
            carrier.RequiresScanToShip = requiresScan;
            changedFields.Add("requiresScanToShip");
        }

        if (request.IsActive is bool isActive && isActive != carrier.IsActive)
        {
            carrier.IsActive = isActive;
            changedFields.Add("isActive");
        }

        if (request.SortOrder is int sortOrder && sortOrder != carrier.SortOrder)
        {
            carrier.SortOrder = sortOrder;
            changedFields.Add("sortOrder");
        }

        if (request.Notes is not null)
        {
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes;
            if (notes != carrier.Notes) { carrier.Notes = notes; changedFields.Add("notes"); }
        }

        if (changedFields.Count > 0)
        {
            db.LogActivityAt("updated",
                $"Carrier {carrier.Name}: updated {changedFields.Count} field(s): {ActivityChangeSummary.Fields(changedFields)}",
                ("Carrier", carrier.Id));
            await db.SaveChangesAsync(cancellationToken);
        }

        return new CarrierListItemModel(
            carrier.Id, carrier.Name, carrier.Code, carrier.Scac,
            carrier.IntegrationKind.ToString(), carrier.DeliveryUpdateMode.ToString(),
            carrier.IntegrationServiceId, carrier.RequiresScanToShip, carrier.IsActive, carrier.SortOrder,
            carrier.CredentialClientId != null && carrier.CredentialSecret != null,
            carrier.CredentialClientId,
            carrier.CredentialEnvironment,
            carrier.Notes);
    }
}
