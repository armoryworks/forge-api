using System.Linq.Expressions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

/// <summary>
/// The SQL-translatable selectors, filters and sorts behind <see cref="JobListFields"/>. Each selector reads
/// the same value the list projection returns, so a filter or sort on the server matches what the client
/// would compute over the rows it shows.
/// </summary>
internal sealed class JobListExpressions(AppDbContext db, DateTimeOffset startOfToday)
{
    private static readonly Expression<Func<Job, decimal?>> QuantitySelector = j =>
        j.JobParts.Any(jp => jp.PartId == j.PartId)
            ? (decimal?)j.JobParts.Where(jp => jp.PartId == j.PartId).Sum(jp => jp.Quantity)
            : j.SalesOrderLineId != null
                ? (decimal?)j.SalesOrderLine!.Quantity
                : j.PartId != null
                    ? 1m
                    : null;

    private static readonly Expression<Func<Job, string?>> BillingStatusSelector = j =>
        j.CompletedDate == null
            ? null
            : j.SalesOrderLine != null && j.SalesOrderLine.SalesOrder.Invoices.Any()
                ? "Invoiced"
                : "Uninvoiced";

    public Expression<Func<Job, string?>>? Text(string key) => key switch
    {
        "jobNumber" => j => j.JobNumber,
        "title" => j => j.Title,
        "partNumber" => j => j.Part != null ? j.Part.PartNumber : null,
        "parentJobNumber" => j => j.ParentJob != null ? j.ParentJob.JobNumber : null,
        "customerName" => j => j.Customer != null ? j.Customer.Name : null,
        "salesOrderNumber" => j => j.SalesOrderLine != null ? j.SalesOrderLine.SalesOrder.OrderNumber : null,
        "externalRef" => j => j.ExternalRef,
        "billingStatus" => BillingStatusSelector,
        "trackTypeId" => j => j.TrackType.Name,
        "assigneeId" => j => db.Users.Where(u => u.Id == j.AssigneeId).Select(u => u.LastName + ", " + u.FirstName).FirstOrDefault(),
        _ => null,
    };

    public static Expression<Func<Job, DateTimeOffset?>>? Date(string key) => key switch
    {
        "dueDate" => j => j.DueDate,
        "startDate" => j => j.StartDate,
        "completedDate" => j => j.CompletedDate,
        "createdAt" => j => j.CreatedAt,
        "updatedAt" => j => j.UpdatedAt,
        _ => null,
    };

    public static Expression<Func<Job, decimal?>>? Number(string key) => key switch
    {
        "id" => j => j.Id,
        "quantity" => QuantitySelector,
        "targetQuantity" => j => j.TargetQuantity,
        "childJobCount" => j => j.ChildJobs.Count(c => c.DeletedAt == null),
        "estimatedTotalCost" => j => j.EstimatedMaterialCost + j.EstimatedLaborCost + j.EstimatedBurdenCost + j.EstimatedSubcontractCost,
        "quotedPrice" => j => j.QuotedPrice,
        "priorityName" => j => (int)j.Priority,
        "disposition" => j => (int?)j.Disposition,
        "currentStageId" => j => j.CurrentStage.SortOrder,
        _ => null,
    };

    public Expression<Func<Job, bool>>? Flag(string key) => key switch
    {
        "isOverdue" => j => j.DueDate != null && j.DueDate < startOfToday && j.CompletedDate == null && j.Disposition == null,
        "onHold" => j => db.StatusEntries.Any(se =>
            se.EntityType == "Job" && se.EntityId == j.Id && se.Category == "hold" && se.EndedAt == null),
        _ => null,
    };

    public Expression<Func<Job, bool>>? Filter(JobListFilter filter)
    {
        var key = filter.Field.Key;
        return filter.Field.Kind switch
        {
            JobListFieldKind.Text => Text(key) is { } text ? TextFilter(text, filter.Operator, filter.Value) : null,
            JobListFieldKind.Date => Date(key) is { } date && JobListFilterValues.DateRange(filter.Value) is { } dates
                ? DateFilter(date, dates.From, dates.ToExclusive)
                : null,
            JobListFieldKind.Number => Number(key) is { } number && JobListFilterValues.NumberRange(filter.Value) is { } numbers
                ? NumberFilter(number, numbers.Min, numbers.Max)
                : null,
            JobListFieldKind.Boolean => Flag(key) is { } flag && JobListFilterValues.Booleans(filter.Value) is { } flags
                ? FlagFilter(flag, flags)
                : null,
            JobListFieldKind.Enum => EnumFilter(key, filter.Value),
            _ => null,
        };
    }

    public IOrderedQueryable<Job>? Sort(IQueryable<Job> q, string key, bool desc)
    {
        if (Text(key) is { } text) return NullsLast(q, text, desc);
        if (Date(key) is { } date) return NullsLast(q, date, desc);
        if (Number(key) is { } number) return NullsLast(q, number, desc);
        if (Flag(key) is { } flag) return desc ? q.OrderByDescending(flag) : q.OrderBy(flag);
        return null;
    }

    private static IOrderedQueryable<Job> NullsLast<T>(IQueryable<Job> q, Expression<Func<Job, T>> selector, bool desc)
    {
        var isNull = Expression.Lambda<Func<Job, bool>>(
            Expression.Equal(selector.Body, Expression.Constant(null, typeof(T))), selector.Parameters);
        var ordered = q.OrderBy(isNull);
        return desc ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector);
    }

    private static Expression<Func<Job, bool>> TextFilter(Expression<Func<Job, string?>> selector, string op, string value)
    {
        var term = value.Trim().ToLower();
        var lowered = ParameterReplacer.Compose<Job, string?, string>(selector, s => (s ?? "").ToLower());
        Expression<Func<string, bool>> test = op switch
        {
            "equals" => s => s == term,
            "startswith" => s => s.StartsWith(term),
            "endswith" => s => s.EndsWith(term),
            "notcontains" => s => !s.Contains(term),
            "notequals" => s => s != term,
            _ => s => s.Contains(term),
        };
        return ParameterReplacer.Compose(lowered, test);
    }

    private static Expression<Func<Job, bool>> DateFilter(
        Expression<Func<Job, DateTimeOffset?>> selector, DateTimeOffset? from, DateTimeOffset? toExclusive)
    {
        Expression<Func<DateTimeOffset?, bool>> test = from.HasValue && toExclusive.HasValue
            ? d => d >= from && d < toExclusive
            : from.HasValue
                ? d => d >= from
                : d => d < toExclusive;
        return ParameterReplacer.Compose(selector, test);
    }

    private static Expression<Func<Job, bool>> NumberFilter(Expression<Func<Job, decimal?>> selector, decimal? min, decimal? max)
    {
        Expression<Func<decimal?, bool>> test = min.HasValue && max.HasValue
            ? n => n >= min && n <= max
            : min.HasValue
                ? n => n >= min
                : n => n <= max;
        return ParameterReplacer.Compose(selector, test);
    }

    private static Expression<Func<Job, bool>>? FlagFilter(Expression<Func<Job, bool>> selector, IReadOnlyList<bool> flags)
    {
        if (flags.Contains(true) && flags.Contains(false)) return null;
        var wanted = flags[0];
        return ParameterReplacer.Compose<Job, bool, bool>(selector, b => b == wanted);
    }

    private static Expression<Func<Job, bool>>? EnumFilter(string key, string value)
    {
        var entries = JobListFilterValues.List(value);
        switch (key)
        {
            case "priorityName":
                var priorities = entries.Select(e => Enum.Parse<JobPriority>(e, true)).ToList();
                return j => priorities.Contains(j.Priority);
            case "disposition":
                var dispositions = entries.Select(e => Enum.Parse<JobDisposition>(e, true)).ToList();
                return j => j.Disposition != null && dispositions.Contains(j.Disposition.Value);
            case "billingStatus":
                var statuses = JobListFilterValues.BillingStatuses
                    .Where(s => entries.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
                return ParameterReplacer.Compose<Job, string?, bool>(BillingStatusSelector, s => s != null && statuses.Contains(s));
        }

        if (JobListFilterValues.Ids(value) is not { } ids) return null;
        return key switch
        {
            "currentStageId" => j => ids.Contains(j.CurrentStageId),
            "trackTypeId" => j => ids.Contains(j.TrackTypeId),
            "assigneeId" => j => j.AssigneeId != null && ids.Contains(j.AssigneeId.Value),
            _ => null,
        };
    }
}
