using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record GetSavedReportsQuery : IRequest<List<SavedReportResponseModel>>;

public class GetSavedReportsHandler(
    IReportBuilderRepository repository,
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<GetSavedReportsQuery, List<SavedReportResponseModel>>
{
    public async Task<List<SavedReportResponseModel>> Handle(GetSavedReportsQuery request, CancellationToken cancellationToken)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var userReports = await repository.GetUserReports(userId);
        var sharedReports = await repository.GetSharedReports();

        var allReports = userReports
            .Union(sharedReports)
            .DistinctBy(r => r.Id)
            .OrderByDescending(r => r.UpdatedAt)
            .ToList();

        var userIds = allReports.Select(r => r.UserId).Distinct().ToList();
        var userNames = new Dictionary<int, string?>();
        foreach (var uid in userIds)
        {
            var user = await userManager.FindByIdAsync(uid.ToString());
            userNames[uid] = user?.UserName;
        }

        return allReports.Select(r => MapToResponse(r, userNames.GetValueOrDefault(r.UserId))).ToList();
    }

    private static readonly JsonSerializerOptions FilterJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static SavedReportResponseModel MapToResponse(SavedReport report, string? userName = null)
    {
        var columns = JsonSerializer.Deserialize<string[]>(report.ColumnsJson) ?? [];
        var filters = !string.IsNullOrEmpty(report.FiltersJson)
            ? JsonSerializer.Deserialize<ReportFilterModel[]>(report.FiltersJson, FilterJsonOptions) ?? []
            : [];

        return new SavedReportResponseModel(
            report.Id,
            report.Name,
            report.Description,
            report.EntitySource,
            columns,
            filters,
            report.GroupByField,
            report.SortField,
            report.SortDirection,
            report.ChartType,
            report.ChartLabelField,
            report.ChartValueField,
            report.IsShared,
            report.UserId,
            userName,
            report.CreatedAt,
            report.UpdatedAt);
    }
}
