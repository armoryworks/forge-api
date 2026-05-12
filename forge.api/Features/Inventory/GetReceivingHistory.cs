using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetReceivingHistoryQuery(int? PurchaseOrderId, int? PartId, int Take = 50) : IRequest<List<ReceivingRecordResponseModel>>;

public class GetReceivingHistoryHandler(IInventoryRepository repo)
    : IRequestHandler<GetReceivingHistoryQuery, List<ReceivingRecordResponseModel>>
{
    public Task<List<ReceivingRecordResponseModel>> Handle(
        GetReceivingHistoryQuery request, CancellationToken cancellationToken)
        => repo.GetReceivingHistoryAsync(request.PurchaseOrderId, request.PartId, request.Take, cancellationToken);
}
