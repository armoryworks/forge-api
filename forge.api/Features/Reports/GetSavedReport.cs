using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetSavedReportQuery(int Id) : IRequest<SavedReportResponseModel>;

public class GetSavedReportHandler(
    IReportBuilderRepository repository,
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<GetSavedReportQuery, SavedReportResponseModel>
{
    public async Task<SavedReportResponseModel> Handle(GetSavedReportQuery request, CancellationToken cancellationToken)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var report = await repository.GetById(request.Id)
            ?? throw new KeyNotFoundException($"Saved report {request.Id} not found.");

        // Only owner or shared reports can be viewed
        if (report.UserId != int.Parse(userId) && !report.IsShared)
            throw new KeyNotFoundException($"Saved report {request.Id} not found.");

        var user = await userManager.FindByIdAsync(report.UserId.ToString());
        return GetSavedReportsHandler.MapToResponse(report, user?.UserName);
    }
}
