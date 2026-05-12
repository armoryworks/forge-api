using System.Security.Claims;

using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Reports;

public record GetMyExpenseHistoryQuery(DateTimeOffset Start, DateTimeOffset End) : IRequest<List<MyExpenseHistoryReportItem>>;

public class GetMyExpenseHistoryHandler(IReportRepository repo, IHttpContextAccessor http)
    : IRequestHandler<GetMyExpenseHistoryQuery, List<MyExpenseHistoryReportItem>>
{
    public async Task<List<MyExpenseHistoryReportItem>> Handle(GetMyExpenseHistoryQuery request, CancellationToken ct)
    {
        var userId = int.Parse(http.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await repo.GetMyExpenseHistoryAsync(userId, request.Start, request.End, ct);
    }
}
