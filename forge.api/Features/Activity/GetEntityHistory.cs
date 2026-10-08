using System.Globalization;
using System.Text;

using MediatR;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

using Microsoft.EntityFrameworkCore;

namespace Forge.Api.Features.Activity;

public record GetEntityHistoryQuery(string EntityType, int EntityId) : IRequest<List<ActivityResponseModel>>;

public class GetEntityHistoryHandler(AppDbContext db)
    : IRequestHandler<GetEntityHistoryQuery, List<ActivityResponseModel>>
{
    private const string FieldChangedAction = "FieldChanged";
    private const string EmptyValue = "(none)";

    private static readonly string[] HiddenFields = ["BoardPosition", "Version"];

    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.Ordinal)
    {
        ["AssigneeId"] = "Assignee",
        ["CurrentStageId"] = "Stage",
        ["CustomerId"] = "Customer",
        ["VendorId"] = "Vendor",
        ["PartId"] = "Part",
        ["CurrentBomRevisionId"] = "BOM Revision",
        ["TrackTypeId"] = "Order Type",
    };

    public async Task<List<ActivityResponseModel>> Handle(GetEntityHistoryQuery request, CancellationToken ct)
    {
        var logs = await db.ActivityLogs
            .Where(a => a.EntityType == request.EntityType
                && a.EntityId == request.EntityId
                && a.Action != "Comment"
                && (a.FieldName == null || !HiddenFields.Contains(a.FieldName)))
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var actorIds = logs
            .Where(l => l.UserId.HasValue)
            .Select(l => l.UserId!.Value)
            .Distinct()
            .ToList();

        var actors = actorIds.Count > 0
            ? await db.Users
                .Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ct)
            : [];

        var references = await ResolveReferencesAsync(logs, ct);

        return logs.Select(l =>
        {
            var actor = l.UserId.HasValue && actors.TryGetValue(l.UserId.Value, out var u) ? u : null;
            var isFieldChange = l.Action == FieldChangedAction && l.FieldName is not null;
            var oldValue = isFieldChange ? DisplayValue(l.FieldName!, l.OldValue, references) : l.OldValue;
            var newValue = isFieldChange ? DisplayValue(l.FieldName!, l.NewValue, references) : l.NewValue;
            var description = isFieldChange
                ? $"{FieldLabel(l.FieldName!)}: {oldValue} → {newValue}"
                : l.Description;

            return new ActivityResponseModel(
                l.Id,
                l.Action,
                l.FieldName,
                oldValue,
                newValue,
                description,
                actor?.Initials,
                actor is not null ? $"{actor.FirstName} {actor.LastName}".Trim() : null,
                l.CreatedAt);
        }).ToList();
    }

    private async Task<Dictionary<string, Dictionary<int, string>>> ResolveReferencesAsync(
        List<ActivityLog> logs, CancellationToken ct)
    {
        var idsByField = logs
            .Where(l => l.Action == FieldChangedAction && l.FieldName is not null && FieldLabels.ContainsKey(l.FieldName))
            .GroupBy(l => l.FieldName!)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(l => new[] { ParseId(l.OldValue), ParseId(l.NewValue) })
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToList());

        var result = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
        foreach (var (field, ids) in idsByField)
        {
            if (ids.Count == 0)
                continue;
            result[field] = await LookupNamesAsync(field, ids, ct);
        }
        return result;
    }

    private async Task<Dictionary<int, string>> LookupNamesAsync(string field, List<int> ids, CancellationToken ct)
    {
        switch (field)
        {
            case "AssigneeId":
                var users = await db.Users
                    .Where(u => ids.Contains(u.Id))
                    .ToListAsync(ct);
                return users.ToDictionary(u => u.Id, u => u.GetDisplayName());
            case "CurrentStageId":
                return await db.JobStages.IgnoreQueryFilters()
                    .Where(s => ids.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            case "CustomerId":
                return await db.Customers.IgnoreQueryFilters()
                    .Where(c => ids.Contains(c.Id))
                    .ToDictionaryAsync(
                        c => c.Id,
                        c => string.IsNullOrWhiteSpace(c.CompanyName) ? c.Name : c.CompanyName,
                        ct);
            case "VendorId":
                return await db.Vendors.IgnoreQueryFilters()
                    .Where(v => ids.Contains(v.Id))
                    .ToDictionaryAsync(v => v.Id, v => v.CompanyName, ct);
            case "PartId":
                return await db.Parts.IgnoreQueryFilters()
                    .Where(p => ids.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.PartNumber, ct);
            case "CurrentBomRevisionId":
                var revisions = await db.BomRevisions.IgnoreQueryFilters()
                    .Where(r => ids.Contains(r.Id))
                    .Select(r => new { r.Id, r.RevisionNumber })
                    .ToListAsync(ct);
                return revisions.ToDictionary(r => r.Id, r => $"Rev {r.RevisionNumber}");
            case "TrackTypeId":
                return await db.TrackTypes.IgnoreQueryFilters()
                    .Where(t => ids.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            default:
                return [];
        }
    }

    private static string DisplayValue(
        string field, string? value, Dictionary<string, Dictionary<int, string>> references)
    {
        if (string.IsNullOrWhiteSpace(value))
            return EmptyValue;

        if (references.TryGetValue(field, out var names)
            && ParseId(value) is int id
            && names.TryGetValue(id, out var name))
            return name;

        return value;
    }

    private static int? ParseId(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static string FieldLabel(string field)
    {
        if (FieldLabels.TryGetValue(field, out var label))
            return label;

        var name = field.Length > 2 && field.EndsWith("Id", StringComparison.Ordinal)
            ? field[..^2]
            : field;

        var words = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])
                && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                words.Append(' ');
            words.Append(name[i]);
        }
        return words.ToString();
    }
}
