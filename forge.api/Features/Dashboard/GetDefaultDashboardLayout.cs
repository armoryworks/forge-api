using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Forge.Data.Context;

namespace Forge.Api.Features.Dashboard;

public record GetDefaultDashboardLayoutQuery : IRequest<DashboardLayoutResponseModel>;

public record DashboardLayoutResponseModel(
    string Role,
    List<string> VisibleWidgets,
    int Columns);

public class GetDefaultDashboardLayoutHandler(
    IHttpContextAccessor httpContext,
    UserManager<ApplicationUser> userManager) : IRequestHandler<GetDefaultDashboardLayoutQuery, DashboardLayoutResponseModel>
{
    private static readonly Dictionary<string, List<string>> RoleWidgets = new()
    {
        ["Admin"] = ["tasks", "stages", "team", "activity", "deadlines", "cycle", "orders", "eod", "margin"],
        ["Manager"] = ["tasks", "stages", "team", "activity", "deadlines", "cycle", "orders", "margin"],
        ["PM"] = ["tasks", "stages", "team", "deadlines", "cycle", "orders", "eod"],
        ["OfficeManager"] = ["tasks", "orders", "deadlines", "activity", "margin"],
        ["Engineer"] = ["tasks", "stages", "deadlines", "cycle", "eod"],
        ["ProductionWorker"] = ["tasks", "deadlines"],
    };

    public async Task<DashboardLayoutResponseModel> Handle(GetDefaultDashboardLayoutQuery request, CancellationToken cancellationToken)
    {
        var userId = httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await userManager.FindByIdAsync(userId);
        var roles = user != null ? await userManager.GetRolesAsync(user) : [];

        // Use highest-privilege role for defaults
        var role = "Engineer";
        foreach (var r in new[] { "Admin", "Manager", "PM", "OfficeManager", "Engineer", "ProductionWorker" })
        {
            if (roles.Contains(r)) { role = r; break; }
        }

        var widgets = RoleWidgets.GetValueOrDefault(role, RoleWidgets["Engineer"]);
        var columns = role == "ProductionWorker" ? 2 : 3;

        return new DashboardLayoutResponseModel(role, widgets, columns);
    }
}
