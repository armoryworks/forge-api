using MediatR;
using Microsoft.AspNetCore.Http;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;

namespace Forge.Tests.Helpers;

/// <summary>
/// An <see cref="IMediator"/> that runs the real purchase-order receive handler against the given context, for
/// handlers (the scanner receive) that record their receipt through it.
/// </summary>
public static class ReceiptMediator
{
    public static IMediator For(
        AppDbContext db,
        IClock clock,
        IHttpContextAccessor httpContext,
        ICapabilitySnapshotProvider? capabilities = null,
        IReceiptInventoryPostingService? posting = null)
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<ReceiveItemsCommand>(), It.IsAny<CancellationToken>()))
            .Returns((ReceiveItemsCommand c, CancellationToken ct) =>
                new ReceiveItemsHandler(
                    new PurchaseOrderRepository(db), clock, mediator.Object, httpContext, db, posting,
                    new InventoryRepository(db), capabilities).Handle(c, ct));
        return mediator.Object;
    }
}
