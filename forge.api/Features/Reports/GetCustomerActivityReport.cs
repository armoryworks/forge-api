using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetCustomerActivityReportQuery : IRequest<List<CustomerActivityReportItem>>;

public class GetCustomerActivityReportHandler(IReportRepository repo)
    : IRequestHandler<GetCustomerActivityReportQuery, List<CustomerActivityReportItem>>
{
    public async Task<List<CustomerActivityReportItem>> Handle(
        GetCustomerActivityReportQuery request, CancellationToken cancellationToken)
    {
        return await repo.GetCustomerActivityAsync(cancellationToken);
    }
}
