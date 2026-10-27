using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Authorization;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reviews;

public record GetPerformanceReviewQuery(int Id) : IRequest<PerformanceReviewDetailResponseModel>;

public class GetPerformanceReviewHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<GetPerformanceReviewQuery, PerformanceReviewDetailResponseModel>
{
    public async Task<PerformanceReviewDetailResponseModel> Handle(GetPerformanceReviewQuery request, CancellationToken ct)
    {
        var user = http.HttpContext?.User;
        var review = await ReviewAccess.LoadReadableAsync(db, request.Id, user, ct);
        var model = (await ReviewProjection.ToModelsAsync(
            db, [review], CallerAccess.UserId(user), ReviewAccess.IsAdmin(user), ct))[0];

        var history = await db.ActivityLogs.AsNoTracking()
            .Where(a => a.EntityType == "PerformanceReview" && a.EntityId == review.Id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new PerformanceReviewHistoryItemModel(
                a.Action,
                a.Description,
                a.UserId == null ? null : db.Users.Where(u => u.Id == a.UserId).Select(u => u.LastName + ", " + u.FirstName).FirstOrDefault(),
                a.CreatedAt))
            .ToListAsync(ct);

        return new PerformanceReviewDetailResponseModel(model, history);
    }
}
