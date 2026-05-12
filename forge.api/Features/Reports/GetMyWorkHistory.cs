using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetMyWorkHistoryQuery : IRequest<List<MyWorkHistoryReportItem>>;

public class GetMyWorkHistoryHandler(
    IReportRepository repo,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<GetMyWorkHistoryQuery, List<MyWorkHistoryReportItem>>
{
    public async Task<List<MyWorkHistoryReportItem>> Handle(GetMyWorkHistoryQuery request, CancellationToken ct)
    {
        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await repo.GetMyWorkHistoryAsync(userId, ct);
    }
}
