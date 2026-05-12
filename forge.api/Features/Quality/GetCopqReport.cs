using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quality;

public record GetCopqReportQuery(DateOnly StartDate, DateOnly EndDate) : IRequest<CopqReportResponseModel>;

public class GetCopqReportHandler(ICopqService copqService)
    : IRequestHandler<GetCopqReportQuery, CopqReportResponseModel>
{
    public async Task<CopqReportResponseModel> Handle(
        GetCopqReportQuery request, CancellationToken cancellationToken)
    {
        return await copqService.GenerateReportAsync(request.StartDate, request.EndDate, cancellationToken);
    }
}
