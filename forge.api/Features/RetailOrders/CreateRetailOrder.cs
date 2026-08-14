using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.RetailOrders;

/// <summary>
/// Creates a consumer order on a retail or marketplace channel.
///
/// <para>This is the single retail order-creation path: manual entry (walk-in,
/// phone, trade show) and channel importers both send this command. Keeping
/// them on one path is deliberate — it is what stops importers from
/// constructing <see cref="SalesOrder"/> aggregates directly in the
/// integrations layer, which is how the previous e-commerce import bypassed
/// MediatR, activity logging and capability gating.</para>
/// </summary>
public record CreateRetailOrderCommand(CreateRetailOrderRequestModel Model)
    : IRequest<CreateRetailOrderResult>;

/// <summary>
/// The order, plus whether this call created it. Importers replay the same
/// external order after a failed batch, so a replay must return the existing
/// order (200) rather than minting a duplicate or 409-ing — the same idempotent
/// create contract the lead intake relay uses.
/// </summary>
public record CreateRetailOrderResult(SalesOrderListItemModel Order, bool Created);

public class CreateRetailOrderValidator : AbstractValidator<CreateRetailOrderCommand>
{
    public CreateRetailOrderValidator()
    {
        RuleFor(x => x.Model.Buyer.DisplayName)
            .NotEmpty().MaximumLength(200)
            .WithMessage("A retail order needs a buyer name — it is what appears on the pick ticket and the label.");
        RuleFor(x => x.Model.Buyer.ContactEmail).MaximumLength(200);
        RuleFor(x => x.Model.Buyer.Phone).MaximumLength(50);
        RuleFor(x => x.Model.Buyer.ExternalBuyerId).MaximumLength(200);

        RuleFor(x => x.Model.ShipTo.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Model.ShipTo.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Model.ShipTo.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Model.ShipTo.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Model.ShipTo.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Model.ShipTo.Country).NotEmpty().MaximumLength(10);

        RuleFor(x => x.Model.ExternalOrderNumber).MaximumLength(100);
        RuleFor(x => x.Model.TaxRate).GreaterThanOrEqualTo(0).LessThan(1);
        RuleFor(x => x.Model.ShippingAmount).GreaterThanOrEqualTo(0m).When(x => x.Model.ShippingAmount.HasValue);

        RuleFor(x => x.Model.Lines).NotEmpty().WithMessage("At least one line item is required");
        RuleForEach(x => x.Model.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Description).NotEmpty().MaximumLength(500);
            line.RuleFor(l => l.Quantity).GreaterThan(0m);
            // Zero is legal — free gifts, promotional inclusions and marketplace
            // replacement orders all legitimately price a line at nothing.
            line.RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0m);
        });
    }
}

public class CreateRetailOrderHandler(
    AppDbContext db,
    ISalesChannelResolver channelResolver,
    ISalesOrderRepository orderRepo,
    IBarcodeService barcodeService,
    IClock clock)
    : IRequestHandler<CreateRetailOrderCommand, CreateRetailOrderResult>
{
    public async Task<CreateRetailOrderResult> Handle(CreateRetailOrderCommand request, CancellationToken ct)
    {
        var model = request.Model;
        var channel = await channelResolver.ResolveAsync(model.ChannelId, ct);

        if (!channel.IsRetail)
        {
            throw new InvalidOperationException(
                $"Channel '{channel.Code}' is {channel.ChannelType}. Retail orders need a DirectRetail or " +
                "Marketplace channel — account orders go through the standard sales-order path, which " +
                "applies credit terms and quoting.");
        }

        if (!channel.IsActive)
            throw new InvalidOperationException($"Channel '{channel.Code}' is inactive and cannot take new orders.");

        // Idempotent replay. Scoped to the channel because external order
        // numbers are only unique within a marketplace.
        if (!string.IsNullOrWhiteSpace(model.ExternalOrderNumber))
        {
            var existing = await db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(
                    o => o.ChannelId == channel.Id && o.ExternalOrderNumber == model.ExternalOrderNumber, ct);

            if (existing is not null)
                return new CreateRetailOrderResult(Project(existing), Created: false);
        }

        var soldToCustomerId = await channelResolver.ResolveSoldToCustomerIdAsync(channel, null, ct);
        var orderDate = model.OrderDate ?? clock.UtcNow;
        var buyer = await UpsertBuyerAsync(channel, model, orderDate, ct);

        var prefix = channel.OrderNumberPrefix ?? "SO";
        var orderNumber = await orderRepo.GenerateNextOrderNumberAsync(prefix, ct);

        var order = new SalesOrder
        {
            OrderNumber = orderNumber,
            CustomerId = soldToCustomerId,
            ChannelId = channel.Id,
            RetailBuyerId = buyer.Id,
            ExternalOrderNumber = model.ExternalOrderNumber,
            ExternalId = model.ExternalOrderId,
            Provider = channel.Code,
            TaxRate = model.TaxRate,
            TaxCollectedBy = model.TaxCollectedBy ?? channel.TaxCollectedBy,
            Notes = model.Notes,
            // Retail orders are paid at checkout, so they enter Confirmed rather
            // than Draft — there is no quote to accept and no credit to approve.
            // CreditTerms and CustomerPO stay null by construction: the money is
            // already collected and the marketplace order number, not a PO, is
            // the buyer's reference.
            Status = SalesOrderStatus.Confirmed,
            ConfirmedDate = orderDate,
            ShipTo = MapShipTo(model.ShipTo),
        };

        await AddLinesAsync(order, channel.Id, model, ct);

        await orderRepo.AddAsync(order, ct);
        await orderRepo.SaveChangesAsync(ct);

        await barcodeService.CreateBarcodeAsync(
            BarcodeEntityType.SalesOrder, order.Id, order.OrderNumber, ct);

        var externalRef = string.IsNullOrWhiteSpace(model.ExternalOrderNumber)
            ? string.Empty
            : $" ({model.ExternalOrderNumber})";
        db.LogActivityAt(
            "created",
            $"Retail order {order.OrderNumber}{externalRef} on {channel.Name} for {buyer.DisplayName}",
            ("SalesOrder", order.Id),
            ("SalesChannel", channel.Id),
            ("RetailBuyer", buyer.Id));
        await db.SaveChangesAsync(ct);

        order.Customer = await db.Customers.FirstAsync(c => c.Id == soldToCustomerId, ct);
        return new CreateRetailOrderResult(Project(order), Created: true);
    }

    /// <summary>
    /// Find-or-create the consumer, keyed on (channel, external buyer id).
    /// Manual entry has no external system to key on, so a synthetic id is
    /// minted — each walk-in is a distinct buyer rather than all of them
    /// collapsing onto one blank key.
    /// </summary>
    private async Task<RetailBuyer> UpsertBuyerAsync(
        SalesChannel channel, CreateRetailOrderRequestModel model, DateTimeOffset orderDate, CancellationToken ct)
    {
        var externalId = string.IsNullOrWhiteSpace(model.Buyer.ExternalBuyerId)
            ? $"manual:{Guid.NewGuid():N}"
            : model.Buyer.ExternalBuyerId.Trim();

        var buyer = await db.RetailBuyers
            .FirstOrDefaultAsync(b => b.ChannelId == channel.Id && b.ExternalBuyerId == externalId, ct);

        if (buyer is null)
        {
            buyer = new RetailBuyer
            {
                ChannelId = channel.Id,
                ExternalBuyerId = externalId,
                DisplayName = model.Buyer.DisplayName.Trim(),
                ContactEmail = Trimmed(model.Buyer.ContactEmail),
                Phone = Trimmed(model.Buyer.Phone),
                MarketingConsent = model.Buyer.MarketingConsent,
                FirstOrderAt = orderDate,
                LastOrderAt = orderDate,
                OrderCount = 1,
            };
            db.RetailBuyers.Add(buyer);
            await db.SaveChangesAsync(ct);

            db.LogActivityAt(
                "created",
                $"Retail buyer '{buyer.DisplayName}' first seen on {channel.Name}",
                ("RetailBuyer", buyer.Id),
                ("SalesChannel", channel.Id));
            await db.SaveChangesAsync(ct);
            return buyer;
        }

        // Repeat buyer. Refresh the contact details from the channel (they are
        // the current truth, and marketplace relay emails rotate) but never
        // downgrade consent that was previously granted off the back of an
        // order that simply did not restate it.
        buyer.DisplayName = model.Buyer.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(model.Buyer.ContactEmail))
            buyer.ContactEmail = model.Buyer.ContactEmail.Trim();
        if (!string.IsNullOrWhiteSpace(model.Buyer.Phone))
            buyer.Phone = model.Buyer.Phone.Trim();
        if (model.Buyer.MarketingConsent)
            buyer.MarketingConsent = true;

        buyer.LastOrderAt = orderDate;
        buyer.OrderCount += 1;
        buyer.FirstOrderAt ??= orderDate;

        return buyer;
    }

    /// <summary>
    /// Build order lines, resolving each to a part where possible.
    ///
    /// <para>An unresolvable SKU does NOT fail the order. A marketplace order is
    /// already paid for and already owed to a customer by the time it reaches
    /// us; refusing to import it because a listing was never mapped would leave
    /// a real obligation invisible. The line lands with a null PartId and its
    /// description, and shows up in unmapped-listing triage.</para>
    /// </summary>
    private async Task AddLinesAsync(
        SalesOrder order, int channelId, CreateRetailOrderRequestModel model, CancellationToken ct)
    {
        // Resolve every SKU in one query rather than per line — the efficiency
        // rules prohibit a Where inside a projection or a per-iteration filter.
        var skus = model.Lines
            .Where(l => l.PartId is null && !string.IsNullOrWhiteSpace(l.ExternalSku))
            .Select(l => l.ExternalSku!)
            .Distinct()
            .ToList();

        var skuToPart = skus.Count == 0
            ? []
            : await db.ChannelListings
                .AsNoTracking()
                .Where(cl => cl.ChannelId == channelId
                    && cl.ExternalSku != null
                    && skus.Contains(cl.ExternalSku)
                    && cl.PartId != null)
                .Select(cl => new { Sku = cl.ExternalSku!, PartId = cl.PartId!.Value })
                .ToDictionaryAsync(x => x.Sku, x => x.PartId, ct);

        var lineNumber = 1;
        foreach (var line in model.Lines)
        {
            var partId = line.PartId;
            if (partId is null
                && !string.IsNullOrWhiteSpace(line.ExternalSku)
                && skuToPart.TryGetValue(line.ExternalSku, out var mapped))
            {
                partId = mapped;
            }

            order.Lines.Add(new SalesOrderLine
            {
                PartId = partId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = lineNumber++,
                Notes = string.IsNullOrWhiteSpace(line.ExternalSku)
                    ? line.Notes
                    : $"SKU {line.ExternalSku}{(string.IsNullOrWhiteSpace(line.Notes) ? "" : $" — {line.Notes}")}",
            });
        }

        // Buyer-paid shipping is revenue, so it belongs on the order as its own
        // line rather than being folded into an item price where it would
        // distort per-part margin.
        if (model.ShippingAmount is > 0m)
        {
            order.Lines.Add(new SalesOrderLine
            {
                PartId = null,
                Description = "Shipping",
                Quantity = 1m,
                UnitPrice = model.ShippingAmount.Value,
                LineNumber = lineNumber,
            });
        }
    }

    private static OrderShipTo MapShipTo(OrderShipToInput input) => new()
    {
        Name = input.Name.Trim(),
        Company = Trimmed(input.Company),
        Line1 = input.Line1.Trim(),
        Line2 = Trimmed(input.Line2),
        City = input.City.Trim(),
        State = input.State.Trim(),
        PostalCode = input.PostalCode.Trim(),
        Country = string.IsNullOrWhiteSpace(input.Country) ? "US" : input.Country.Trim(),
        Phone = Trimmed(input.Phone),
        IsValidated = input.IsValidated,
    };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SalesOrderListItemModel Project(SalesOrder order) => new(
        order.Id,
        order.OrderNumber,
        order.CustomerId,
        order.Customer?.Name ?? string.Empty,
        order.Status.ToString(),
        order.CustomerPO,
        order.Lines.Count,
        order.Lines.Sum(l => l.Quantity * l.UnitPrice),
        order.RequestedDeliveryDate,
        order.CreatedAt,
        SalesOrderId: order.Id,
        JobId: null);
}
