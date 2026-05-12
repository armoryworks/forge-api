using System.Security.Claims;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record UpdateSavedReportCommand(
    int Id,
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

public class UpdateSavedReportCommandValidator : AbstractValidator<UpdateSavedReportCommand>
{
    private static readonly string[] ValidEntitySources =
    [
        "Jobs", "Parts", "Customers", "Expenses", "TimeEntries",
        "Invoices", "Leads", "Assets", "PurchaseOrders", "SalesOrders",
        "Quotes", "Shipments", "Inventory"
    ];

    private static readonly string[] ValidChartTypes = ["bar", "line", "pie", "doughnut", "table"];
    private static readonly string[] ValidSortDirections = ["asc", "desc"];

    public UpdateSavedReportCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Report ID is required.");

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

public class UpdateSavedReportHandler(
    IReportBuilderRepository repository,
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor httpContextAccessor) : IRequestHandler<UpdateSavedReportCommand, SavedReportResponseModel>
{
    public async Task<SavedReportResponseModel> Handle(UpdateSavedReportCommand request, CancellationToken cancellationToken)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User not authenticated.");

        var report = await repository.GetById(request.Id)
            ?? throw new KeyNotFoundException($"Saved report {request.Id} not found.");

        if (report.UserId != int.Parse(userId))
            throw new UnauthorizedAccessException("Only the report owner can update this report.");

        report.Name = request.Name;
        report.Description = request.Description;
        report.EntitySource = request.EntitySource;
        report.ColumnsJson = JsonSerializer.Serialize(request.Columns);
        report.FiltersJson = request.Filters != null ? JsonSerializer.Serialize(request.Filters) : null;
        report.GroupByField = request.GroupByField;
        report.SortField = request.SortField;
        report.SortDirection = request.SortDirection;
        report.ChartType = request.ChartType;
        report.ChartLabelField = request.ChartLabelField;
        report.ChartValueField = request.ChartValueField;
        report.IsShared = request.IsShared;

        await repository.Update(report);

        var updated = await repository.GetById(report.Id)
            ?? throw new InvalidOperationException("Failed to retrieve updated report.");

        var user = await userManager.FindByIdAsync(userId);
        return GetSavedReportsHandler.MapToResponse(updated, user?.UserName);
    }
}
