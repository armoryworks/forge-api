using System.Security.Claims;

using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.DomainEvents;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public record CreatePurchaseOrderCommand(
    int VendorId,
    int? JobId,
    string? Notes,
    List<CreatePurchaseOrderLineModel> Lines,
    // Bought-parts effort PR2.5 — header fields. All optional: when omitted,
    // we default Incoterm + QuoteCurrency from the preferred VendorPart of
    // the first part line's part. EstimatedFreight stays null when the buyer
    // doesn't yet have a freight quote (distinct from $0 = free shipping).
    Incoterm? Incoterm = null,
    decimal? EstimatedFreight = null,
    string? QuoteCurrency = null,
    // Optional caller-supplied PO number — gated by purchase_orders.allow_manual_numbers.
    string? PONumber = null,
    DateTimeOffset? ExpectedDeliveryDate = null) : IRequest<PurchaseOrderListItemModel>;

public class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.PartId).GreaterThan(0).When(l => l.PartId.HasValue);
            // A part-less line has no Part to describe it — the description is the line.
            line.RuleFor(l => l.Description)
                .NotEmpty()
                .When(l => l.PartId is null)
                .WithMessage("Description is required when the line has no part.");
            line.RuleFor(l => l.Description).MaximumLength(500);
            line.RuleFor(l => l.Notes).MaximumLength(1000);
            // Phase 3 / WU-10 — Quantity is decimal; allow fractional values
            // (e.g. 0.5 lb of solder), but disallow zero / negative.
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0m);
        });
        RuleFor(x => x.EstimatedFreight)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.EstimatedFreight.HasValue);
        RuleFor(x => x.QuoteCurrency)
            .Length(3)
            .When(x => !string.IsNullOrEmpty(x.QuoteCurrency))
            .WithMessage("QuoteCurrency must be a 3-letter ISO-4217 code");
    }
}

public class CreatePurchaseOrderHandler(
    IPurchaseOrderRepository poRepo,
    IVendorRepository vendorRepo,
    IPartRepository partRepo,
    IBarcodeService barcodeService,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    IMediator mediator,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext db,
    IClock clock)
    : IRequestHandler<CreatePurchaseOrderCommand, PurchaseOrderListItemModel>
{
    // System setting that gates caller-supplied PO numbers. Stored as "true"/"false".
    private const string AllowManualPONumbersKey = "purchase_orders.allow_manual_numbers";

    public async Task<PurchaseOrderListItemModel> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var vendor = await vendorRepo.FindAsync(request.VendorId, cancellationToken);
        // Phase 3 H2 / WU-12: vendor-active check on PO create. Phase 1 found
        // deactivated vendors still accepted new POs (no gate); fixes that
        // gap. NotFound preserved as KeyNotFoundException → 404 via middleware.
        ActiveCheck.EnsureActive(vendor, "Vendor", "vendorId", request.VendorId);

        if (request.JobId is int jobId)
            await EnsureJobOpenAsync(jobId, cancellationToken);

        var poNumber = await ResolvePONumberAsync(request, cancellationToken);

        // Bought-parts PR2.5 — derive Incoterm/QuoteCurrency defaults from
        // the preferred VendorPart for the first part line's (vendor, part) when
        // not supplied by the caller. Falls back to entity defaults when no
        // VendorPart row exists yet (FOB_Origin / USD).
        Incoterm? defaultIncoterm = null;
        string? defaultCurrency = null;
        DateTimeOffset? defaultExpectedDelivery = null;
        if (request.Lines.FirstOrDefault(l => l.PartId is not null)?.PartId is int firstPartId
            && (!request.Incoterm.HasValue
                || string.IsNullOrEmpty(request.QuoteCurrency)
                || !request.ExpectedDeliveryDate.HasValue))
        {
            var vp = await db.VendorParts
                .AsNoTracking()
                .Where(x => x.VendorId == request.VendorId && x.PartId == firstPartId)
                .OrderByDescending(x => x.IsPreferred)
                .Select(x => new { x.Incoterm, x.Currency, x.LeadTimeDays })
                .FirstOrDefaultAsync(cancellationToken);
            if (vp != null)
            {
                defaultIncoterm = vp.Incoterm;
                defaultCurrency = vp.Currency;
                if (vp.LeadTimeDays is int leadTimeDays)
                    defaultExpectedDelivery = new DateTimeOffset(
                        clock.UtcNow.UtcDateTime.Date.AddDays(leadTimeDays), TimeSpan.Zero);
            }
        }

        // S4b provenance — this handler is the manual-entry path: stamp
        // Manual + the creating user from the JWT claims. Kiosk/system calls
        // without a user principal leave OriginUserId null.
        var userId = int.Parse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            VendorId = request.VendorId,
            JobId = request.JobId,
            Notes = request.Notes,
            Incoterm = request.Incoterm ?? defaultIncoterm ?? Incoterm.FOB_Origin,
            EstimatedFreight = request.EstimatedFreight,
            QuoteCurrency = request.QuoteCurrency ?? defaultCurrency ?? "USD",
            ExpectedDeliveryDate = request.ExpectedDeliveryDate ?? defaultExpectedDelivery,
            OriginSource = PoOriginSource.Manual,
            OriginUserId = userId > 0 ? userId : null,
        };

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            Part? part = null;
            if (line.PartId is int linePartId)
            {
                part = await partRepo.FindAsync(linePartId, cancellationToken);
                // Phase 3 H2 / WU-12: part-active check on PO line. Obsolete parts
                // are blocked from new POs; UI already filters them on the picker
                // but a previously-loaded form could still target one.
                ActiveCheck.EnsureActive(part, "Part", $"lines[{i}].partId", linePartId);
            }

            po.Lines.Add(new PurchaseOrderLine
            {
                PartId = line.PartId,
                // Part-less lines validated Description NotEmpty above.
                Description = line.Description ?? part?.Description ?? part?.Name ?? string.Empty,
                OrderedQuantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Notes = line.Notes,
                PurchaseUnitId = line.PurchaseUnitId,
                ManualOverrideReason = line.ManualOverrideReason,
            });
        }

        await poRepo.AddAsync(po, cancellationToken);
        await poRepo.SaveChangesAsync(cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.PurchaseOrder, po.Id, po.PONumber, cancellationToken);

        // Record the number in the identifier registry (history + resolution).
        await identifiers.IssueAsync(BusinessEntityType.PurchaseOrder, po.Id, po.PONumber, cancellationToken);

        db.LogActivityAt(
            "created",
            $"Created purchase order {po.PONumber} for {vendor!.CompanyName} with {po.Lines.Count} line(s)",
            ("PurchaseOrder", po.Id));
        await db.SaveChangesAsync(cancellationToken);

        // Publish domain event for calendar integration
        if (userId > 0)
            await mediator.Publish(new PurchaseOrderCreatedEvent(po.Id, userId), cancellationToken);

        return new PurchaseOrderListItemModel(
            po.Id, po.PONumber, po.VendorId, vendor!.CompanyName,
            po.JobId, null, po.Status.ToString(),
            po.Lines.Count,
            po.Lines.Sum(l => l.OrderedQuantity),
            0, null, po.IsBlanket, po.CreatedAt,
            OriginSource: po.OriginSource.ToString(),
            OriginReference: po.OriginReference);
    }

    private async Task EnsureJobOpenAsync(int jobId, CancellationToken ct)
    {
        var job = await db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new { j.JobNumber, j.IsArchived })
            .FirstOrDefaultAsync(ct);

        var message = job switch
        {
            null => "That work order no longer exists. Choose another work order.",
            { IsArchived: true } => $"Work order {job.JobNumber} is archived. Choose an open work order.",
            _ => null,
        };
        if (message is null) return;

        throw new ValidationException(new[]
        {
            new ValidationFailure("jobId", message) { AttemptedValue = jobId },
        });
    }

    // Uses a caller-supplied PO number when manual numbers are enabled and one
    // was provided; otherwise auto-generates the next sequential number.
    private async Task<string> ResolvePONumberAsync(CreatePurchaseOrderCommand request, CancellationToken ct)
    {
        var supplied = request.PONumber?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && await ManualPONumbersAllowedAsync(ct))
        {
            if (await poRepo.PONumberExistsAsync(supplied, null, ct))
                throw new InvalidOperationException($"Purchase order number '{supplied}' is already in use.");
            return supplied;
        }

        return await poRepo.GenerateNextPONumberAsync(ct);
    }

    private async Task<bool> ManualPONumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualPONumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
