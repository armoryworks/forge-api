using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Vendors;

public record GetVendorDropdownQuery : IRequest<List<VendorResponseModel>>;

public class GetVendorDropdownHandler(IVendorRepository repo)
    : IRequestHandler<GetVendorDropdownQuery, List<VendorResponseModel>>
{
    public async Task<List<VendorResponseModel>> Handle(GetVendorDropdownQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetAllActiveAsync(cancellationToken);
    }
}
