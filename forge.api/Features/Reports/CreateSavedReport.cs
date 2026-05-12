using System.Security.Claims;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record CreateSavedReportCommand(
    string Name,
    string? Description,
    string EntitySource,
    string[] Columns,
    ReportFilterModel[]? Filters,
    string? GroupByField,
    string? SortField,
    string? SortDirection,
    string? ChartType,
    string? ChartLabelField,
    string? ChartValueField,
    bool IsShared) : IRequest<SavedReportResponseModel>;

public class CreateSavedReportCommandValidator : AbstractValidator<CreateSavedReportCommand>
{
    private static readonly string[] ValidEntitySources =
    [
        "Jobs", "Parts", "Customers", "Expenses", "TimeEntries",
        "Invoices", "Leads", "Assets", "PurchaseOrders", "SalesOrders",
        "Quotes", "Shipments", "Inventory"
    ];

    private static readonly string[] ValidChartTypes = ["bar", "line", "pie", "doughnut", "table"];
    private static readonly string[] ValidSortDirections = ["asc", "desc"];

    public CreateSavedReportCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200).WithMessage("Name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.");

        RuleFor(x => x.EntitySource)
            .NotEmpty().WithMessage("Entity source is required.")
            .Must(s => ValidEntitySources.Contains(s))
            .WithMessage("Invalid entity source.");

        RuleFor(x => x.Columns)
            .NotEmpty().WithMessage("At least one column is required.");

        RuleFor(x => x.ChartType)
            .Must(t => t == null || ValidChartTypes.Contains(t))
            .WithMessage("Invalid chart type. Must be: bar, line, pie, doughnut, or table.");

        RuleFor(x => x.SortDirection)
            .Must(d => d == null || ValidSortDirections.Contains(d))
            .WithMessage("Sort direction must be 'asc' or 'desc'.");
    }
}

public class CreateSavedReportHandler(
    IReportBuilderRepository repository,
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<CreateSavedReportCommand, SavedReportResponseModel>
{
    public async Task<SavedReportResponseModel> Handle(CreateSavedReportCommand request, CancellationToken cancellationToken)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var userIdInt = int.Parse(userId);

        var report = new SavedReport
        {
            Name = request.Name,
            Description = request.Description,
            EntitySource = request.EntitySource,
            ColumnsJson = JsonSerializer.Serialize(request.Columns),
            FiltersJson = request.Filters != null ? JsonSerializer.Serialize(request.Filters) : null,
            GroupByField = request.GroupByField,
            SortField = request.SortField,
            SortDirection = request.SortDirection,
            ChartType = request.ChartType,
            ChartLabelField = request.ChartLabelField,
            ChartValueField = request.ChartValueField,
            IsShared = request.IsShared,
            UserId = userIdInt,
        };

        await repository.Create(report);

        var created = await repository.GetById(report.Id)
            ?? throw new InvalidOperationException("Failed to retrieve created report.");

        var user = await userManager.FindByIdAsync(userId);
        return GetSavedReportsHandler.MapToResponse(created, user?.UserName);
    }
}
