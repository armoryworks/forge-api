using MediatR;

using Forge.Core.Models;

namespace Forge.Api.Features.Inventory;

public record GetConsignmentTransactionsQuery(int AgreementId) : IRequest<IReadOnlyList<ConsignmentTransactionResponseModel>>;

public class GetConsignmentTransactionsHandler(IConsignmentStockService consignment)
    : IRequestHandler<GetConsignmentTransactionsQuery, IReadOnlyList<ConsignmentTransactionResponseModel>>
{
    public Task<IReadOnlyList<ConsignmentTransactionResponseModel>> Handle(GetConsignmentTransactionsQuery request, CancellationToken cancellationToken)
        => consignment.GetTransactionsAsync(request.AgreementId, cancellationToken);
}
