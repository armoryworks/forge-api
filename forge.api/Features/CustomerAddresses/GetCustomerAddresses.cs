using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.CustomerAddresses;

public record GetCustomerAddressesQuery(int CustomerId) : IRequest<List<CustomerAddressResponseModel>>;

public class GetCustomerAddressesHandler(ICustomerAddressRepository repo)
    : IRequestHandler<GetCustomerAddressesQuery, List<CustomerAddressResponseModel>>
{
    public async Task<List<CustomerAddressResponseModel>> Handle(GetCustomerAddressesQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetByCustomerAsync(request.CustomerId, cancellationToken);
    }
}
