using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.AssignmentRules;

/// <summary>
/// Phase 1r / Batch 11 — assignment rule CRUD. Order returned by GET is
/// ascending Priority then alpha by name so the admin UI lists rules in
/// the same order the matching engine evaluates them.
/// </summary>
public record GetAssignmentRulesQuery(bool? ActiveOnly) : IRequest<List<AssignmentRuleResponseModel>>;

public class GetAssignmentRulesHandler(AppDbContext db)
    : IRequestHandler<GetAssignmentRulesQuery, List<AssignmentRuleResponseModel>>
{
    public async Task<List<AssignmentRuleResponseModel>> Handle(GetAssignmentRulesQuery request, CancellationToken ct)
    {
        var query = db.AssignmentRules.AsNoTracking();
        if (request.ActiveOnly == true) query = query.Where(r => r.IsActive);

        var rules = await query.OrderBy(r => r.Priority).ThenBy(r => r.Name).ToListAsync(ct);
        return rules.Select(r => new AssignmentRuleResponseModel(
            r.Id, r.Name, r.Kind, r.Priority, r.IsActive, r.Spec, r.CreatedAt)).ToList();
    }
}

public record CreateAssignmentRuleCommand(CreateAssignmentRuleRequest Request) : IRequest<AssignmentRuleResponseModel>;

public class CreateAssignmentRuleHandler(AppDbContext db)
    : IRequestHandler<CreateAssignmentRuleCommand, AssignmentRuleResponseModel>
{
    public async Task<AssignmentRuleResponseModel> Handle(CreateAssignmentRuleCommand request, CancellationToken ct)
    {
        var r = request.Request;
        if (string.IsNullOrWhiteSpace(r.Name)) throw new InvalidOperationException("Rule name is required.");

        var rule = new AssignmentRule
        {
            Name = r.Name.Trim(),
            Kind = r.Kind,
            Priority = r.Priority,
            IsActive = true,
            Spec = r.Spec?.Trim(),
        };
        db.AssignmentRules.Add(rule);
        db.LogActivityAt("assignment-rule-created",
            $"Created assignment rule '{rule.Name}' ({rule.Kind}, priority {rule.Priority}).",
            ("AssignmentRule", 0));
        await db.SaveChangesAsync(ct);
        return new AssignmentRuleResponseModel(
            rule.Id, rule.Name, rule.Kind, rule.Priority, rule.IsActive, rule.Spec, rule.CreatedAt);
    }
}

public record UpdateAssignmentRuleCommand(int Id, UpdateAssignmentRuleRequest Request) : IRequest<AssignmentRuleResponseModel>;

public class UpdateAssignmentRuleHandler(AppDbContext db)
    : IRequestHandler<UpdateAssignmentRuleCommand, AssignmentRuleResponseModel>
{
    public async Task<AssignmentRuleResponseModel> Handle(UpdateAssignmentRuleCommand request, CancellationToken ct)
    {
        var rule = await db.AssignmentRules.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Assignment rule {request.Id} not found.");

        var r = request.Request;
        var changed = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Name) && r.Name.Trim() != rule.Name)
        {
            rule.Name = r.Name.Trim();
            changed.Add("name");
        }
        if (r.Kind.HasValue && r.Kind.Value != rule.Kind)
        {
            rule.Kind = r.Kind.Value;
            changed.Add($"kind: {rule.Kind}");
        }
        if (r.Priority.HasValue && r.Priority.Value != rule.Priority)
        {
            rule.Priority = r.Priority.Value;
            changed.Add($"priority: {rule.Priority}");
        }
        if (r.IsActive != rule.IsActive)
        {
            rule.IsActive = r.IsActive;
            changed.Add($"isActive: {rule.IsActive}");
        }
        if (r.Spec?.Trim() != rule.Spec)
        {
            rule.Spec = r.Spec?.Trim();
            changed.Add("spec");
        }

        if (changed.Count > 0)
        {
            db.LogActivityAt("assignment-rule-updated",
                $"Updated {changed.Count} field(s): {string.Join(", ", changed)}",
                ("AssignmentRule", rule.Id));
        }

        await db.SaveChangesAsync(ct);
        return new AssignmentRuleResponseModel(
            rule.Id, rule.Name, rule.Kind, rule.Priority, rule.IsActive, rule.Spec, rule.CreatedAt);
    }
}

public record DeleteAssignmentRuleCommand(int Id) : IRequest;

public class DeleteAssignmentRuleHandler(AppDbContext db) : IRequestHandler<DeleteAssignmentRuleCommand>
{
    public async Task Handle(DeleteAssignmentRuleCommand request, CancellationToken ct)
    {
        var rule = await db.AssignmentRules.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Assignment rule {request.Id} not found.");

        rule.DeletedAt = DateTimeOffset.UtcNow;
        db.LogActivityAt("assignment-rule-deleted",
            $"Deleted assignment rule '{rule.Name}'.",
            ("AssignmentRule", rule.Id));
        await db.SaveChangesAsync(ct);
    }
}
