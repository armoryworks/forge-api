using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using QBEngineer.Api.Features.DomainEvents;
using QBEngineer.Api.Validation;
using QBEngineer.Core.Entities;
using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Core.Models;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Features.PurchaseOrders;

public record CreatePurchaseOrderCommand(
    int VendorId,
    int? JobId,
    string? Notes,
    List<CreatePurchaseOrderLineModel> Lines,
    // Bought-parts effort PR2.5 — header fields. All optional: when omitted,
    // we default Incoterm + QuoteCurrency from the preferred VendorPart of
    // the first line's part. EstimatedFreight stays null when the buyer
    // doesn't yet have a freight quote (distinct from $0 = free shipping).
    Incoterm? Incoterm = null,
    decimal? EstimatedFreight = null,
    string? QuoteCurrency = null) : IRequest<PurchaseOrderListItemModel>;

public class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderValidator()
    {
        RuleFor(x => x.VendorId).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.PartId).GreaterThan(0);
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
    IMediator mediator,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext db)
    : IRequestHandler<CreatePurchaseOrderCommand, PurchaseOrderListItemModel>
{
    public async Task<PurchaseOrderListItemModel> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var vendor = await vendorRepo.FindAsync(request.VendorId, cancellationToken);
        // Phase 3 H2 / WU-12: vendor-active check on PO create. Phase 1 found
        // deactivated vendors still accepted new POs (no gate); fixes that
        // gap. NotFound preserved as KeyNotFoundException → 404 via middleware.
        ActiveCheck.EnsureActive(vendor, "Vendor", "vendorId", request.VendorId);

        var poNumber = await poRepo.GenerateNextPONumberAsync(cancellationToken);

        // Bought-parts PR2.5 — derive Incoterm/QuoteCurrency defaults from
        // the preferred VendorPart for the first line's (vendor, part) when
        // not supplied by the caller. Falls back to entity defaults when no
        // VendorPart row exists yet (FOB_Origin / USD).
        Incoterm? defaultIncoterm = null;
        string? defaultCurrency = null;
        if (request.Lines.Count > 0
            && (!request.Incoterm.HasValue || string.IsNullOrEmpty(request.QuoteCurrency)))
        {
            var firstPartId = request.Lines[0].PartId;
            var vp = await db.VendorParts
                .AsNoTracking()
                .Where(x => x.VendorId == request.VendorId && x.PartId == firstPartId)
                .Select(x => new { x.Incoterm, x.Currency })
                .FirstOrDefaultAsync(cancellationToken);
            if (vp != null)
            {
                defaultIncoterm = vp.Incoterm;
                defaultCurrency = vp.Currency;
            }
        }

        var po = new PurchaseOrder
        {
            PONumber = poNumber,
            VendorId = request.VendorId,
            JobId = request.JobId,
            Notes = request.Notes,
            Incoterm = request.Incoterm ?? defaultIncoterm ?? Incoterm.FOB_Origin,
            EstimatedFreight = request.EstimatedFreight,
            QuoteCurrency = request.QuoteCurrency ?? defaultCurrency ?? "USD",
        };

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            var part = await partRepo.FindAsync(line.PartId, cancellationToken);
            // Phase 3 H2 / WU-12: part-active check on PO line. Obsolete parts
            // are blocked from new POs; UI already filters them on the picker
            // but a previously-loaded form could still target one.
            ActiveCheck.EnsureActive(part, "Part", $"lines[{i}].partId", line.PartId);

            po.Lines.Add(new PurchaseOrderLine
            {
                PartId = line.PartId,
                Description = line.Description ?? part!.Description ?? part.Name,
                OrderedQuantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Notes = line.Notes,
            });
        }

        await poRepo.AddAsync(po, cancellationToken);
        await poRepo.SaveChangesAsync(cancellationToken);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.PurchaseOrder, po.Id, po.PONumber, cancellationToken);

        // Publish domain event for calendar integration
        var userId = int.Parse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        if (userId > 0)
            await mediator.Publish(new PurchaseOrderCreatedEvent(po.Id, userId), cancellationToken);

        return new PurchaseOrderListItemModel(
            po.Id, po.PONumber, po.VendorId, vendor!.CompanyName,
            po.JobId, null, po.Status.ToString(),
            po.Lines.Count,
            po.Lines.Sum(l => l.OrderedQuantity),
            0, null, po.IsBlanket, po.CreatedAt);
    }
}
