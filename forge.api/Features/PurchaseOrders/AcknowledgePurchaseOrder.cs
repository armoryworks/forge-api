using System.Globalization;

using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.PurchaseOrders;

public record AcknowledgePurchaseOrderCommand(int Id, DateTimeOffset? ExpectedDeliveryDate) : IRequest;

public class AcknowledgePurchaseOrderHandler(IPurchaseOrderRepository repo, IClock clock, AppDbContext db)
    : IRequestHandler<AcknowledgePurchaseOrderCommand>
{
    public async Task Handle(AcknowledgePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var po = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order {request.Id} not found");

        if (po.Status != PurchaseOrderStatus.Submitted)
            throw new InvalidOperationException("Only Submitted purchase orders can be acknowledged");

        po.Status = PurchaseOrderStatus.Acknowledged;
        po.AcknowledgedDate = clock.UtcNow;

        var description = "Acknowledged";
        if (request.ExpectedDeliveryDate.HasValue)
        {
            description = $"Acknowledged; vendor promised {FormatDate(request.ExpectedDeliveryDate.Value)}";
            if (po.ExpectedDeliveryDate.HasValue && po.ExpectedDeliveryDate != request.ExpectedDeliveryDate)
                description += $" (was {FormatDate(po.ExpectedDeliveryDate.Value)})";
            po.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        }

        db.LogActivityAt("acknowledged", description, ("PurchaseOrder", po.Id));

        await repo.SaveChangesAsync(cancellationToken);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
}
