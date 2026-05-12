using System.Linq.Expressions;
using System.Reflection;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Reports;

public record RunReportCommand(
    string EntitySource,
    string[] Columns,
    ReportFilterModel[]? Filters,
    string? GroupByField,
    string? SortField,
    string? SortDirection,
    int? Page,
    int? PageSize) : IRequest<RunReportResponseModel>;

public class RunReportCommandValidator : AbstractValidator<RunReportCommand>
{
    private static readonly string[] ValidEntitySources =
    [
        "Jobs", "Parts", "Customers", "Expenses", "TimeEntries",
        "Invoices", "Leads", "Assets", "PurchaseOrders", "SalesOrders",
        "Quotes", "Shipments", "Inventory", "Payments", "Vendors",
        "ProductionRuns", "LotRecords", "QcInspections",
        "MaintenanceSchedules", "MaintenanceLogs", "DowntimeLogs",
        "CustomerReturns", "BinMovements", "PlanningCycles",
        "InvoiceLines", "SalesOrderLines", "PurchaseOrderLines", "QuoteLines"
    ];

    public RunReportCommandValidator()
    {
        RuleFor(x => x.EntitySource)
            .NotEmpty().WithMessage("Entity source is required.")
            .Must(s => ValidEntitySources.Contains(s))
            .WithMessage("Invalid entity source.");

        RuleFor(x => x.Columns)
            .NotEmpty().WithMessage("At least one column is required.");

        RuleFor(x => x.PageSize)
            .LessThanOrEqualTo(1000).WithMessage("Page size must not exceed 1000.")
            .When(x => x.PageSize.HasValue);
    }
}

public class RunReportHandler(AppDbContext context) : IRequestHandler<RunReportCommand, RunReportResponseModel>
{
    public async Task<RunReportResponseModel> Handle(RunReportCommand request, CancellationToken cancellationToken)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? 100;

        return request.EntitySource switch
        {
            "Jobs" => await ExecuteReport(
                context.Jobs
                    .Include(j => j.Customer)
                    .Include(j => j.TrackType)
                    .Include(j => j.CurrentStage)
                    .Include(j => j.Part)
                    .Include(j => j.ParentJob)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Parts" => await ExecuteReport(
                context.Parts
                    .Include(p => p.PreferredVendor)
                    .Include(p => p.ToolingAsset)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Customers" => await ExecuteReport(
                context.Customers.AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Expenses" => await ExecuteReport(
                context.Expenses
                    .Include(e => e.Job)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "TimeEntries" => await ExecuteReport(
                context.TimeEntries
                    .Include(t => t.Job)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Invoices" => await ExecuteReport(
                context.Invoices
                    .Include(i => i.Customer)
                    .Include(i => i.SalesOrder)
                    .Include(i => i.Shipment)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Leads" => await ExecuteReport(
                context.Leads
                    .Include(l => l.ConvertedCustomer)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Assets" => await ExecuteReport(
                context.Assets
                    .Include(a => a.SourceJob)
                    .Include(a => a.SourcePart)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "PurchaseOrders" => await ExecuteReport(
                context.PurchaseOrders
                    .Include(p => p.Vendor)
                    .Include(p => p.Job)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "SalesOrders" => await ExecuteReport(
                context.SalesOrders
                    .Include(s => s.Customer)
                    .Include(s => s.Quote)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Quotes" => await ExecuteReport(
                context.Quotes
                    .Include(q => q.Customer)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Shipments" => await ExecuteReport(
                context.Shipments
                    .Include(s => s.SalesOrder)
                        .ThenInclude(so => so.Customer)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Inventory" => await ExecuteReport(
                context.BinContents
                    .Include(b => b.Location)
                    .Include(b => b.Job)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Payments" => await ExecuteReport(
                context.Payments
                    .Include(p => p.Customer)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "Vendors" => await ExecuteReport(
                context.Vendors.AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "ProductionRuns" => await ExecuteReport(
                context.ProductionRuns
                    .Include(pr => pr.Job)
                    .Include(pr => pr.Part)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "LotRecords" => await ExecuteReport(
                context.LotRecords
                    .Include(lr => lr.Part)
                    .Include(lr => lr.Job)
                    .Include(lr => lr.ProductionRun)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "QcInspections" => await ExecuteReport(
                context.QcInspections
                    .Include(qi => qi.Job)
                    .Include(qi => qi.ProductionRun)
                        .ThenInclude(pr => pr!.Part)
                    .Include(qi => qi.Template)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "MaintenanceSchedules" => await ExecuteReport(
                context.MaintenanceSchedules
                    .Include(ms => ms.Asset)
                    .Include(ms => ms.MaintenanceJob)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "MaintenanceLogs" => await ExecuteReport(
                context.MaintenanceLogs
                    .Include(ml => ml.Schedule)
                        .ThenInclude(s => s.Asset)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "DowntimeLogs" => await ExecuteReport(
                context.DowntimeLogs
                    .Include(dl => dl.Asset)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "CustomerReturns" => await ExecuteReport(
                context.CustomerReturns
                    .Include(cr => cr.Customer)
                    .Include(cr => cr.OriginalJob)
                    .Include(cr => cr.ReworkJob)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "BinMovements" => await ExecuteReport(
                context.BinMovements
                    .Include(bm => bm.FromLocation)
                    .Include(bm => bm.ToLocation)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "PlanningCycles" => await ExecuteReport(
                context.PlanningCycles.AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "InvoiceLines" => await ExecuteReport(
                context.InvoiceLines
                    .Include(il => il.Invoice)
                        .ThenInclude(i => i.Customer)
                    .Include(il => il.Part)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "SalesOrderLines" => await ExecuteReport(
                context.SalesOrderLines
                    .Include(sol => sol.SalesOrder)
                        .ThenInclude(so => so.Customer)
                    .Include(sol => sol.Part)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "PurchaseOrderLines" => await ExecuteReport(
                context.PurchaseOrderLines
                    .Include(pol => pol.PurchaseOrder)
                        .ThenInclude(po => po.Vendor)
                    .Include(pol => pol.Part)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            "QuoteLines" => await ExecuteReport(
                context.QuoteLines
                    .Include(ql => ql.Quote)
                        .ThenInclude(q => q.Customer)
                    .Include(ql => ql.Part)
                    .AsNoTracking(),
                request, page, pageSize, cancellationToken),
            _ => throw new ArgumentException($"Unknown entity source: {request.EntitySource}")
        };
    }

    private static async Task<RunReportResponseModel> ExecuteReport<T>(
        IQueryable<T> query,
        RunReportCommand request,
        int page,
        int pageSize,
        CancellationToken cancellationToken) where T : class
    {
        // Apply filters
        if (request.Filters is { Length: > 0 })
        {
            foreach (var filter in request.Filters)
            {
                query = ApplyFilter(query, filter);
            }
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply sorting
        if (!string.IsNullOrEmpty(request.SortField))
        {
            query = ApplySort(query, request.SortField, request.SortDirection ?? "asc");
        }

        // Apply pagination
        query = query.Skip((page - 1) * pageSize).Take(pageSize);

        // Materialize
        var entities = await query.ToListAsync(cancellationToken);

        // Project to dictionaries
        var rows = entities.Select(e => ProjectToDictionary(e, request.Columns)).ToList();

        // Apply grouping
        Dictionary<string, List<Dictionary<string, object?>>>? groupedData = null;
        if (!string.IsNullOrEmpty(request.GroupByField))
        {
            groupedData = rows
                .GroupBy(row => row.TryGetValue(request.GroupByField, out var val) ? val?.ToString() ?? "(null)" : "(null)")
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        return new RunReportResponseModel(request.Columns, rows, totalCount, groupedData);
    }

    private static IQueryable<T> ApplyFilter<T>(IQueryable<T> query, ReportFilterModel filter) where T : class
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        var property = GetNestedProperty(parameter, filter.Field);

        if (property == null)
            return query;

        Expression? predicate = filter.Operator switch
        {
            ReportFilterOperator.IsNull => Expression.Equal(property, Expression.Constant(null, property.Type)),
            ReportFilterOperator.IsNotNull => Expression.NotEqual(property, Expression.Constant(null, property.Type)),
            _ => BuildComparisonExpression(property, filter)
        };

        if (predicate == null)
            return query;

        var lambda = Expression.Lambda<Func<T, bool>>(predicate, parameter);
        return query.Where(lambda);
    }

    private static Expression? BuildComparisonExpression(Expression property, ReportFilterModel filter)
    {
        if (filter.Value == null && filter.Operator != ReportFilterOperator.IsNull && filter.Operator != ReportFilterOperator.IsNotNull)
            return null;

        var underlyingType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
        var accessExpr = property;

        // For nullable types, access the .Value for comparisons (except null checks)
        if (Nullable.GetUnderlyingType(property.Type) != null)
        {
            var hasValue = Expression.Property(property, "HasValue");
            accessExpr = Expression.Property(property, "Value");

            var innerComparison = BuildCoreComparison(accessExpr, underlyingType, filter);
            if (innerComparison == null) return null;

            return Expression.AndAlso(hasValue, innerComparison);
        }

        return BuildCoreComparison(accessExpr, underlyingType, filter);
    }

    private static Expression? BuildCoreComparison(Expression property, Type propertyType, ReportFilterModel filter)
    {
        if (filter.Value == null) return null;

        if (propertyType == typeof(string))
            return BuildStringComparison(property, filter);

        if (propertyType == typeof(DateTimeOffset) || propertyType == typeof(DateOnly))
            return BuildDateComparison(property, propertyType, filter);

        if (propertyType == typeof(decimal) || propertyType == typeof(int) || propertyType == typeof(double) || propertyType == typeof(float) || propertyType == typeof(long))
            return BuildNumericComparison(property, propertyType, filter);

        if (propertyType == typeof(bool))
            return BuildBoolComparison(property, filter);

        if (propertyType.IsEnum)
            return BuildEnumComparison(property, propertyType, filter);

        return null;
    }

    private static Expression? BuildStringComparison(Expression property, ReportFilterModel filter)
    {
        var value = Expression.Constant(filter.Value);

        return filter.Operator switch
        {
            ReportFilterOperator.Equals => Expression.Equal(property, value),
            ReportFilterOperator.NotEquals => Expression.NotEqual(property, value),
            ReportFilterOperator.Contains => Expression.Call(property, typeof(string).GetMethod("Contains", [typeof(string)])!, value),
            ReportFilterOperator.StartsWith => Expression.Call(property, typeof(string).GetMethod("StartsWith", [typeof(string)])!, value),
            ReportFilterOperator.In => filter.Value != null ? BuildInExpression(property, filter.Value, typeof(string)) : null,
            _ => null
        };
    }

    private static Expression? BuildDateComparison(Expression property, Type propertyType, ReportFilterModel filter)
    {
        if (propertyType == typeof(DateOnly))
        {
            if (!DateOnly.TryParse(filter.Value, out var dateValue)) return null;
            var value = Expression.Constant(dateValue);

            if (filter.Operator == ReportFilterOperator.Between)
            {
                if (filter.Value2 == null || !DateOnly.TryParse(filter.Value2, out var dateValue2)) return null;
                var value2 = Expression.Constant(dateValue2);
                return Expression.AndAlso(
                    Expression.GreaterThanOrEqual(property, value),
                    Expression.LessThanOrEqual(property, value2));
            }

            return filter.Operator switch
            {
                ReportFilterOperator.Equals => Expression.Equal(property, value),
                ReportFilterOperator.NotEquals => Expression.NotEqual(property, value),
                ReportFilterOperator.GreaterThan => Expression.GreaterThan(property, value),
                ReportFilterOperator.LessThan => Expression.LessThan(property, value),
                ReportFilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(property, value),
                ReportFilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(property, value),
                _ => null
            };
        }
        else
        {
            if (!DateTimeOffset.TryParse(filter.Value, out var dateValue)) return null;
            // DateTimeOffset already carries UTC offset — no SpecifyKind needed
            var value = Expression.Constant(dateValue);

            if (filter.Operator == ReportFilterOperator.Between)
            {
                if (filter.Value2 == null || !DateTimeOffset.TryParse(filter.Value2, out var dateValue2)) return null;
                // DateTimeOffset already carries UTC offset — no SpecifyKind needed
                var value2 = Expression.Constant(dateValue2);
                return Expression.AndAlso(
                    Expression.GreaterThanOrEqual(property, value),
                    Expression.LessThanOrEqual(property, value2));
            }

            return filter.Operator switch
            {
                ReportFilterOperator.Equals => Expression.Equal(property, value),
                ReportFilterOperator.NotEquals => Expression.NotEqual(property, value),
                ReportFilterOperator.GreaterThan => Expression.GreaterThan(property, value),
                ReportFilterOperator.LessThan => Expression.LessThan(property, value),
                ReportFilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(property, value),
                ReportFilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(property, value),
                _ => null
            };
        }
    }

    private static Expression? BuildNumericComparison(Expression property, Type propertyType, ReportFilterModel filter)
    {
        object? parsedValue = TryParseNumeric(filter.Value!, propertyType);
        if (parsedValue == null) return null;

        var value = Expression.Constant(parsedValue, propertyType);

        if (filter.Operator == ReportFilterOperator.Between)
        {
            object? parsedValue2 = filter.Value2 != null ? TryParseNumeric(filter.Value2, propertyType) : null;
            if (parsedValue2 == null) return null;
            var value2 = Expression.Constant(parsedValue2, propertyType);
            return Expression.AndAlso(
                Expression.GreaterThanOrEqual(property, value),
                Expression.LessThanOrEqual(property, value2));
        }

        return filter.Operator switch
        {
            ReportFilterOperator.Equals => Expression.Equal(property, value),
            ReportFilterOperator.NotEquals => Expression.NotEqual(property, value),
            ReportFilterOperator.GreaterThan => Expression.GreaterThan(property, value),
            ReportFilterOperator.LessThan => Expression.LessThan(property, value),
            ReportFilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(property, value),
            ReportFilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(property, value),
            ReportFilterOperator.In => BuildInExpression(property, filter.Value!, propertyType),
            _ => null
        };
    }

    private static Expression? BuildBoolComparison(Expression property, ReportFilterModel filter)
    {
        if (!bool.TryParse(filter.Value, out var boolValue)) return null;
        var value = Expression.Constant(boolValue);
        return Expression.Equal(property, value);
    }

    private static Expression? BuildEnumComparison(Expression property, Type enumType, ReportFilterModel filter)
    {
        if (!Enum.TryParse(enumType, filter.Value, ignoreCase: true, out var enumValue)) return null;
        var value = Expression.Constant(enumValue, enumType);

        return filter.Operator switch
        {
            ReportFilterOperator.Equals => Expression.Equal(property, value),
            ReportFilterOperator.NotEquals => Expression.NotEqual(property, value),
            ReportFilterOperator.In => BuildEnumInExpression(property, filter.Value!, enumType),
            _ => null
        };
    }

    private static Expression? BuildInExpression(Expression property, string valueStr, Type elementType)
    {
        var values = valueStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (elementType == typeof(string))
        {
            var list = values.ToList();
            var containsMethod = typeof(List<string>).GetMethod("Contains", [typeof(string)])!;
            return Expression.Call(Expression.Constant(list), containsMethod, property);
        }

        var parsedValues = values
            .Select(v => TryParseNumeric(v, elementType))
            .Where(v => v != null)
            .ToList();

        if (parsedValues.Count == 0) return null;

        // Build: list.Contains(property)
        var listType = typeof(List<>).MakeGenericType(elementType);
        var listInstance = Activator.CreateInstance(listType)!;
        var addMethod = listType.GetMethod("Add")!;
        foreach (var val in parsedValues)
            addMethod.Invoke(listInstance, [val]);

        var containsMethodGeneric = listType.GetMethod("Contains", [elementType])!;
        return Expression.Call(Expression.Constant(listInstance), containsMethodGeneric, property);
    }

    private static Expression? BuildEnumInExpression(Expression property, string valueStr, Type enumType)
    {
        var values = valueStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsedValues = values
            .Select(v => Enum.TryParse(enumType, v, ignoreCase: true, out var result) ? result : null)
            .Where(v => v != null)
            .ToList();

        if (parsedValues.Count == 0) return null;

        var listType = typeof(List<>).MakeGenericType(enumType);
        var listInstance = Activator.CreateInstance(listType)!;
        var addMethod = listType.GetMethod("Add")!;
        foreach (var val in parsedValues)
            addMethod.Invoke(listInstance, [val]);

        var containsMethod = listType.GetMethod("Contains", [enumType])!;
        return Expression.Call(Expression.Constant(listInstance), containsMethod, property);
    }

    private static object? TryParseNumeric(string value, Type type)
    {
        if (type == typeof(int) && int.TryParse(value, out var intVal)) return intVal;
        if (type == typeof(decimal) && decimal.TryParse(value, out var decVal)) return decVal;
        if (type == typeof(double) && double.TryParse(value, out var dblVal)) return dblVal;
        if (type == typeof(float) && float.TryParse(value, out var fltVal)) return fltVal;
        if (type == typeof(long) && long.TryParse(value, out var lngVal)) return lngVal;
        return null;
    }

    private static Expression? GetNestedProperty(Expression parameter, string propertyPath)
    {
        Expression current = parameter;
        foreach (var segment in propertyPath.Split('.'))
        {
            var prop = current.Type.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) return null;
            current = Expression.Property(current, prop);
        }
        return current;
    }

    private static IQueryable<T> ApplySort<T>(IQueryable<T> query, string sortField, string direction) where T : class
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        var property = GetNestedProperty(parameter, sortField);
        if (property == null) return query;

        var lambda = Expression.Lambda(property, parameter);
        var methodName = direction.Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? "OrderByDescending"
            : "OrderBy";

        var method = typeof(Queryable).GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), property.Type);

        return (IQueryable<T>)method.Invoke(null, [query, lambda])!;
    }

    private static Dictionary<string, object?> ProjectToDictionary<T>(T entity, string[] columns)
    {
        var dict = new Dictionary<string, object?>();
        var type = typeof(T);

        foreach (var column in columns)
        {
            dict[column] = GetNestedPropertyValue(entity!, column);
        }

        return dict;
    }

    private static object? GetNestedPropertyValue(object entity, string propertyPath)
    {
        object? current = entity;
        foreach (var segment in propertyPath.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }

        // Convert enums to their string name for JSON serialization
        if (current != null && current.GetType().IsEnum)
            return current.ToString();

        return current;
    }
}
