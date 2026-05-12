using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Create a new VendorPart row linking a Vendor to a Part with
/// vendor-scoped sourcing metadata. Enforces:
///  - Vendor + Part both exist (KeyNotFoundException → 404 otherwise).
///  - (VendorId, PartId) uniqueness — duplicate POST → 409.
///  - At-most-one-preferred-per-Part — if IsPreferred=true is requested, any
///    other VendorPart for the same Part has its IsPreferred cleared in the
///    same SaveChanges.
/// </summary>
public record CreateVendorPartCommand(CreateVendorPartRequestModel Body)
    : IRequest<VendorPartResponseModel>;

public class CreateVendorPartValidator : AbstractValidator<CreateVendorPartCommand>
{
    public CreateVendorPartValidator()
    {
        RuleFor(x => x.Body.VendorId).GreaterThan(0);
        RuleFor(x => x.Body.PartId).GreaterThan(0);
        RuleFor(x => x.Body.VendorPartNumber).MaximumLength(100);
        RuleFor(x => x.Body.ManufacturerName).MaximumLength(200);
        RuleFor(x => x.Body.VendorMpn).MaximumLength(100);
        RuleFor(x => x.Body.CountryOfOrigin).MaximumLength(2);
        RuleFor(x => x.Body.HtsCode).MaximumLength(20);
        RuleFor(x => x.Body.Notes).MaximumLength(2000);
        RuleFor(x => x.Body.LeadTimeDays).GreaterThanOrEqualTo(0).When(x => x.Body.LeadTimeDays.HasValue);
        RuleFor(x => x.Body.MinOrderQty).GreaterThanOrEqualTo(0).When(x => x.Body.MinOrderQty.HasValue);
        RuleFor(x => x.Body.PackSize).GreaterThanOrEqualTo(0).When(x => x.Body.PackSize.HasValue);
    }
}

public class CreateVendorPartHandler(AppDbContext db)
    : IRequestHandler<CreateVendorPartCommand, VendorPartResponseModel>
{
    public async Task<VendorPartResponseModel> Handle(CreateVendorPartCommand request, CancellationToken ct)
    {
        var body = request.Body;

        // Pre-flight existence checks → 404 if either parent is missing.
        var vendorExists = await db.Vendors.AnyAsync(v => v.Id == body.VendorId, ct);
        if (!vendorExists)
            throw new KeyNotFoundException($"Vendor {body.VendorId} not found");

        var partExists = await db.Parts.AnyAsync(p => p.Id == body.PartId, ct);
        if (!partExists)
            throw new KeyNotFoundException($"Part {body.PartId} not found");

        // Pre-flight uniqueness check → 409 if a VendorPart already binds
        // this (vendor, part) pair. We check ahead of SaveChanges (rather
        // than catching DbUpdateException) so the InMemory provider used in
        // tests gives the same behavior as the Postgres unique index.
        var duplicate = await db.VendorParts
            .AnyAsync(vp => vp.VendorId == body.VendorId && vp.PartId == body.PartId, ct);
        if (duplicate)
            throw new InvalidOperationException("VendorPart already exists for this vendor + part.");

        var vp = new VendorPart
        {
            VendorId = body.VendorId,
            PartId = body.PartId,
            VendorPartNumber = body.VendorPartNumber?.Trim(),
            ManufacturerName = body.ManufacturerName?.Trim(),
            VendorMpn = body.VendorMpn?.Trim(),
            LeadTimeDays = body.LeadTimeDays,
            MinOrderQty = body.MinOrderQty,
            PackSize = body.PackSize,
            CountryOfOrigin = body.CountryOfOrigin?.Trim().ToUpperInvariant(),
            HtsCode = body.HtsCode?.Trim(),
            IsApproved = body.IsApproved,
            IsPreferred = body.IsPreferred,
            Certifications = body.Certifications,
            LastQuotedDate = body.LastQuotedDate,
            Notes = body.Notes,
            Currency = string.IsNullOrWhiteSpace(body.Currency)
                ? "USD"
                : body.Currency.Trim().ToUpperInvariant(),
        };

        // At-most-one-preferred-per-Part — atomically clear preferred on any
        // sibling VendorPart for the same Part before inserting the new row.
        if (body.IsPreferred)
        {
            var siblings = await db.VendorParts
                .Where(other => other.PartId == body.PartId && other.IsPreferred)
                .ToListAsync(ct);
            foreach (var sib in siblings)
                sib.IsPreferred = false;
        }

        db.VendorParts.Add(vp);
        await db.SaveChangesAsync(ct);

        // Reload with Vendor + Part + tiers so the response mapper can flatten
        // the navigation-property names.
        var loaded = await db.VendorParts
            .Include(x => x.Vendor)
            .Include(x => x.Part)
            .Include(x => x.PriceTiers)
            .AsNoTracking()
            .FirstAsync(x => x.Id == vp.Id, ct);

        // Indexing-points rule: this row sits between Part and Vendor — log on
        // both so the change is visible from either entity's Activity tab.
        var preferredSuffix = body.IsPreferred ? " (preferred)" : "";
        db.LogActivityAt(
            "vendor-source-added",
            $"Added vendor source: {loaded.Vendor.CompanyName}{preferredSuffix}",
            ("Part", body.PartId),
            ("Vendor", body.VendorId));
        await db.SaveChangesAsync(ct);

        return VendorPartMapper.ToResponse(loaded);
    }
}
