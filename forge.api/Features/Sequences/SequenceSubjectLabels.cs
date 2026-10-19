using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Sequences;

/// <summary>Resolves the document number shown for a sequence instance's subject (a job's number, not its database id).</summary>
public static class SequenceSubjectLabels
{
    public static async Task<Dictionary<(string EntityType, int EntityId), string>> ResolveAsync(
        AppDbContext db, IEnumerable<SequenceInstance> instances, CancellationToken ct)
    {
        var subjects = instances
            .Where(i => i.SubjectEntityType is not null && i.SubjectEntityId.HasValue)
            .Select(i => (Type: i.SubjectEntityType!, Id: i.SubjectEntityId!.Value))
            .Distinct()
            .ToList();
        var labels = new Dictionary<(string, int), string>();

        foreach (var group in subjects.GroupBy(s => s.Type))
        {
            var ids = group.Select(s => s.Id).ToList();
            var found = group.Key switch
            {
                "Job" => await db.Jobs.AsNoTracking().Where(j => ids.Contains(j.Id))
                    .Select(j => new { j.Id, Label = j.JobNumber }).ToDictionaryAsync(x => x.Id, x => x.Label, ct),
                "SalesOrder" => await db.SalesOrders.AsNoTracking().Where(o => ids.Contains(o.Id))
                    .Select(o => new { o.Id, Label = o.OrderNumber }).ToDictionaryAsync(x => x.Id, x => x.Label, ct),
                "PurchaseOrder" => await db.PurchaseOrders.AsNoTracking().Where(p => ids.Contains(p.Id))
                    .Select(p => new { p.Id, Label = p.PONumber }).ToDictionaryAsync(x => x.Id, x => x.Label, ct),
                "Part" => await db.Parts.AsNoTracking().Where(p => ids.Contains(p.Id))
                    .Select(p => new { p.Id, Label = p.PartNumber }).ToDictionaryAsync(x => x.Id, x => x.Label, ct),
                "Customer" => await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
                    .Select(c => new { c.Id, Label = c.CompanyName ?? c.Name }).ToDictionaryAsync(x => x.Id, x => x.Label, ct),
                "Vendor" => await db.Vendors.AsNoTracking().Where(v => ids.Contains(v.Id))
                    .Select(v => new { v.Id, Label = v.CompanyName }).ToDictionaryAsync(x => x.Id, x => x.Label, ct),
                _ => new Dictionary<int, string>(),
            };
            foreach (var (id, label) in found)
                labels[(group.Key, id)] = label;
        }
        return labels;
    }

    public static async Task<SequenceInstanceResponseModel> ToModelAsync(
        AppDbContext db, SequenceInstance instance, CancellationToken ct)
    {
        var labels = await ResolveAsync(db, [instance], ct);
        return SequenceMapping.ToModel(instance, Lookup(labels, instance));
    }

    public static string? Lookup(Dictionary<(string EntityType, int EntityId), string> labels, SequenceInstance instance) =>
        instance.SubjectEntityType is string type && instance.SubjectEntityId is int id
            ? labels.GetValueOrDefault((type, id))
            : null;
}
