using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.CustomerReturns;

/// <summary>
/// Records a return that was authorised on a sales channel rather than in Forge.
///
/// <para>The direction of control is the whole difference. A B2B RMA is a
/// decision we make: the customer asks, we inspect, we authorise. A marketplace
/// return is a decision already made — the buyer clicked "return" on Amazon and
/// the platform approved it under its own policy. By the time we hear about it
/// the refund may already have been issued. So this handler records rather than
/// adjudicates, and it never rejects a return for lacking an inspection.</para>
///
/// <para>It also does not require a job. <see cref="CreateCustomerReturnCommand"/>
/// takes a non-null <c>OriginalJobId</c> because a made-to-order part is always
/// traceable to one; a retail return is usually a stocked item picked from a
/// bin, and is identified by its order line instead.</para>
/// </summary>
public record CreateChannelReturnCommand(CreateChannelReturnRequestModel Model)
    : IRequest<CreateChannelReturnResult>;

/// <summary>Created is false when the RMA had already been imported — connectors replay.</summary>
public record CreateChannelReturnResult(CustomerReturnListItemModel Return, bool Created);

public class CreateChannelReturnValidator : AbstractValidator<CreateChannelReturnCommand>
{
    public CreateChannelReturnValidator()
    {
        RuleFor(x => x.Model.SalesOrderId).GreaterThan(0);
        RuleFor(x => x.Model.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Model.Notes).MaximumLength(2000);
        RuleFor(x => x.Model.ExternalRmaId).MaximumLength(200);
        RuleFor(x => x.Model.Quantity).GreaterThan(0m).When(x => x.Model.Quantity.HasValue);
        RuleFor(x => x.Model.RefundAmount).GreaterThanOrEqualTo(0m).When(x => x.Model.RefundAmount.HasValue);
    }
}

public class CreateChannelReturnHandler(AppDbContext db, IClock clock)
    : IRequestHandler<CreateChannelReturnCommand, CreateChannelReturnResult>
{
    public async Task<CreateChannelReturnResult> Handle(CreateChannelReturnCommand request, CancellationToken ct)
    {
        var model = request.Model;

        var order = await db.SalesOrders
            .Include(o => o.Lines)
            .Include(o => o.Customer)
            .Include(o => o.Channel)
            .FirstOrDefaultAsync(o => o.Id == model.SalesOrderId, ct)
            ?? throw new KeyNotFoundException($"SalesOrder {model.SalesOrderId} not found");

        var channelId = order.ChannelId
            ?? throw new InvalidOperationException(
                $"Order {order.OrderNumber} is not on a sales channel — use the standard RMA path.");

        // Idempotent replay, matching the order-import contract.
        if (!string.IsNullOrWhiteSpace(model.ExternalRmaId))
        {
            var existing = await db.CustomerReturns
                .AsNoTracking()
                .Include(r => r.Customer)
                .FirstOrDefaultAsync(
                    r => r.ChannelId == channelId && r.ExternalRmaId == model.ExternalRmaId, ct);

            if (existing is not null)
                return new CreateChannelReturnResult(Project(existing), Created: false);
        }

        SalesOrderLine? line = null;
        if (model.SalesOrderLineId is int lineId)
        {
            line = order.Lines.FirstOrDefault(l => l.Id == lineId)
                ?? throw new InvalidOperationException(
                    $"Line {lineId} does not belong to order {order.OrderNumber}.");
        }
        else if (order.Lines.Count == 1)
        {
            // Single-line order: the line is unambiguous, so do not make the
            // caller supply it. Most retail orders are one line.
            line = order.Lines.First();
        }

        var returnNumber = await GenerateReturnNumberAsync(ct);

        var customerReturn = new CustomerReturn
        {
            ReturnNumber = returnNumber,
            // The receivable — and therefore the credit — belongs to the account
            // the order billed to, which on a marketplace is the house account.
            CustomerId = order.CustomerId,
            OriginalJobId = null,
            SalesOrderLineId = line?.Id,
            ChannelId = channelId,
            ExternalRmaId = string.IsNullOrWhiteSpace(model.ExternalRmaId) ? null : model.ExternalRmaId.Trim(),
            Reason = model.Reason.Trim(),
            Notes = model.Notes,
            Quantity = model.Quantity ?? line?.Quantity,
            RefundAmount = model.RefundAmount,
            ReturnDate = model.ReturnDate ?? clock.UtcNow,
            // Authorised by the platform, not by us. Received is the honest
            // starting state: we know it is coming back, we have not inspected it.
            Status = CustomerReturnStatus.Received,
        };

        db.CustomerReturns.Add(customerReturn);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt(
            "channel-return-received",
            $"Return {returnNumber} from {order.Channel?.Name ?? "channel"} against order {order.OrderNumber}"
                + (string.IsNullOrWhiteSpace(customerReturn.ExternalRmaId)
                    ? string.Empty
                    : $" (RMA {customerReturn.ExternalRmaId})"),
            ("CustomerReturn", customerReturn.Id),
            ("SalesOrder", order.Id),
            ("SalesChannel", channelId));
        await db.SaveChangesAsync(ct);

        customerReturn.Customer = order.Customer;
        return new CreateChannelReturnResult(Project(customerReturn), Created: true);
    }

    private async Task<string> GenerateReturnNumberAsync(CancellationToken ct)
    {
        var last = await db.CustomerReturns
            .IgnoreQueryFilters()
            .Where(r => r.ReturnNumber.StartsWith("RMA-"))
            .OrderByDescending(r => r.Id)
            .Select(r => r.ReturnNumber)
            .FirstOrDefaultAsync(ct);

        if (last is not null && int.TryParse(last[4..], out var n))
            return $"RMA-{n + 1:D5}";

        return "RMA-00001";
    }

    private static CustomerReturnListItemModel Project(CustomerReturn r) => new(
        r.Id, r.ReturnNumber, r.CustomerId, r.Customer?.Name ?? string.Empty,
        r.OriginalJobId, null,
        r.ReworkJobId, null,
        r.Status.ToString(), r.Reason, r.ReturnDate, r.CreatedAt);
}
