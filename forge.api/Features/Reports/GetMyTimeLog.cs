using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetMyTimeLogQuery(DateTimeOffset Start, DateTimeOffset End) : IRequest<List<MyTimeLogReportItem>>;

public class GetMyTimeLogHandler(
    IReportRepository repo,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<GetMyTimeLogQuery, List<MyTimeLogReportItem>>
{
    public async Task<List<MyTimeLogReportItem>> Handle(GetMyTimeLogQuery request, CancellationToken ct)
    {
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await repo.GetMyTimeLogAsync(userId, request.Start, request.End, ct);
    }
}
