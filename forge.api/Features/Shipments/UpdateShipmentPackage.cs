using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Shipments;

public record UpdateShipmentPackageCommand(
    int ShipmentId,
    int PackageId,
    string? TrackingNumber,
    string? Carrier,
    decimal? Weight,
    string? Status,
    decimal? Length = null,
    decimal? Width = null,
    decimal? Height = null) : IRequest<ShipmentPackageResponseModel>;

public class UpdateShipmentPackageValidator : AbstractValidator<UpdateShipmentPackageCommand>
{
    public UpdateShipmentPackageValidator()
    {
        RuleFor(x => x.Weight).GreaterThanOrEqualTo(0).When(x => x.Weight.HasValue);
        RuleFor(x => x.Length).GreaterThanOrEqualTo(0).When(x => x.Length.HasValue);
        RuleFor(x => x.Width).GreaterThanOrEqualTo(0).When(x => x.Width.HasValue);
        RuleFor(x => x.Height).GreaterThanOrEqualTo(0).When(x => x.Height.HasValue);
        RuleFor(x => x.TrackingNumber).MaximumLength(200);
    }
}

public class UpdateShipmentPackageHandler(AppDbContext db) : IRequestHandler<UpdateShipmentPackageCommand, ShipmentPackageResponseModel>
{
    public async Task<ShipmentPackageResponseModel> Handle(UpdateShipmentPackageCommand request, CancellationToken ct)
    {
        var package = await db.ShipmentPackages
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && p.ShipmentId == request.ShipmentId, ct)
            ?? throw new KeyNotFoundException($"Package {request.PackageId} not found.");

        if (request.TrackingNumber != null) package.TrackingNumber = request.TrackingNumber;
        if (request.Carrier != null) package.Carrier = request.Carrier;
        if (request.Weight.HasValue) package.Weight = request.Weight;
        if (request.Status != null) package.Status = request.Status;
        if (request.Length.HasValue) package.Length = request.Length;
        if (request.Width.HasValue) package.Width = request.Width;
        if (request.Height.HasValue) package.Height = request.Height;

        db.ChangeTracker.DetectChanges();
        if (ActivityChangeSummary.Updated(ActivityChangeSummary.ChangedProperties(db.Entry(package))) is string summary)
            db.LogActivityAt("package-updated", $"Package {summary.ToLowerInvariant()}", ("Shipment", package.ShipmentId));

        await db.SaveChangesAsync(ct);

        return new ShipmentPackageResponseModel(
            package.Id, package.ShipmentId, package.TrackingNumber, package.Carrier,
            package.Weight, package.Length, package.Width, package.Height, package.Status);
    }
}
