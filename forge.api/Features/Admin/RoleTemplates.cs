using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

// ────────────────────────────────────────────────────────────────────────
// Role-bundle definition CRUD (originally Phase 3 / WU-06 / C1).
//
// A role template is a single named bundle of underlying roles (e.g.,
// "FrontOffice" → OfficeManager + Controller + IT Admin). Direct user
// role-assignment is now multi-role (retired the user-side template
// coupling 2026-07-03), so templates are no longer assigned to users. They
// remain the named bundle a SystemApiKey scopes its grants to — the
// SystemApiKey auth path intersects a key's effective roles with the
// bound template's IncludedRoleNames. "AssigneeCount" below is therefore
// the number of API keys scoped to the bundle.
//
// System-default templates (IsSystemDefault=true) seed at install and are
// protected from edit/delete via the API surface — tenants who want to
// customize them duplicate-and-rename instead.
// ────────────────────────────────────────────────────────────────────────

public record RoleTemplateResponseModel(
    int Id,
    string Name,
    string? Description,
    bool IsSystemDefault,
    string[] IncludedRoleNames,
    int AssigneeCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt);

// ── List ────────────────────────────────────────────────────────────────

public record GetRoleTemplatesQuery(bool IncludeDeactivated = false)
    : IRequest<List<RoleTemplateResponseModel>>;

public class GetRoleTemplatesHandler(AppDbContext db)
    : IRequestHandler<GetRoleTemplatesQuery, List<RoleTemplateResponseModel>>
{
    public async Task<List<RoleTemplateResponseModel>> Handle(
        GetRoleTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = db.RoleTemplates.AsNoTracking();
        if (!request.IncludeDeactivated)
            query = query.Where(t => t.DeactivatedAt == null);

        var rows = await query
            .OrderByDescending(t => t.IsSystemDefault)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        // Compute scoped-API-key counts in one query (the bundle's "assignees").
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.Set<SystemApiKey>()
            .Where(k => k.RoleTemplateId.HasValue && ids.Contains(k.RoleTemplateId.Value))
            .GroupBy(k => k.RoleTemplateId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var countMap = counts.ToDictionary(c => c.Id, c => c.Count);

        return rows.Select(r => MapToResponse(r, countMap.GetValueOrDefault(r.Id, 0))).ToList();
    }

    internal static RoleTemplateResponseModel MapToResponse(RoleTemplate t, int assigneeCount)
    {
        var roles = SafeDeserialize(t.IncludedRoleNamesJson);
        return new RoleTemplateResponseModel(
            t.Id, t.Name, t.Description, t.IsSystemDefault, roles,
            assigneeCount, t.CreatedAt, t.DeactivatedAt);
    }

    internal static string[] SafeDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

// ── Create ──────────────────────────────────────────────────────────────

public record CreateRoleTemplateCommand(
    string Name,
    string? Description,
    string[] IncludedRoleNames) : IRequest<RoleTemplateResponseModel>;

public class CreateRoleTemplateValidator : AbstractValidator<CreateRoleTemplateCommand>
{
    public CreateRoleTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.IncludedRoleNames)
            .NotNull()
            .Must(r => r.Length > 0)
            .WithMessage("Template must include at least one role.")
            .Must(r => r.Length <= 25)
            .WithMessage("Template may include at most 25 roles.");
    }
}

public class CreateRoleTemplateHandler(
    AppDbContext db,
    RoleManager<IdentityRole<int>> roleManager,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<CreateRoleTemplateCommand, RoleTemplateResponseModel>
{
    public async Task<RoleTemplateResponseModel> Handle(
        CreateRoleTemplateCommand request, CancellationToken cancellationToken)
    {
        // Active duplicate-name check. The DB has a unique index on Name to
        // prevent two simultaneous active rows from sharing a name; if a
        // soft-deleted row collides, reactivate-and-update rather than
        // forcing the user to pick a new name.
        var existing = await db.RoleTemplates
            .FirstOrDefaultAsync(t => t.Name == request.Name, cancellationToken);
        if (existing is not null)
        {
            if (existing.DeactivatedAt is null)
                throw new InvalidOperationException(
                    $"A role template named '{request.Name}' already exists.");
            if (existing.IsSystemDefault)
                throw new InvalidOperationException(
                    $"A system-default template '{request.Name}' exists; pick a different name.");
        }

        await EnsureRolesExistAsync(request.IncludedRoleNames, roleManager);

        RoleTemplate entity;
        if (existing is not null)
        {
            // Reactivate the soft-deleted row.
            existing.Description = request.Description;
            existing.IncludedRoleNamesJson = JsonSerializer.Serialize(request.IncludedRoleNames);
            existing.DeactivatedAt = null;
            entity = existing;
        }
        else
        {
            entity = new RoleTemplate
            {
                Name = request.Name,
                Description = request.Description,
                IsSystemDefault = false,
                IncludedRoleNamesJson = JsonSerializer.Serialize(request.IncludedRoleNames),
            };
            db.RoleTemplates.Add(entity);
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync("RoleTemplateCreated", db.CurrentUserId ?? 0,
            entityType: "RoleTemplate", entityId: entity.Id,
            details: JsonSerializer.Serialize(new
            {
                name = entity.Name,
                includedRoles = request.IncludedRoleNames,
            }),
            ct: cancellationToken);

        return GetRoleTemplatesHandler.MapToResponse(entity, 0);
    }

    internal static async Task EnsureRolesExistAsync(
        string[] roleNames, RoleManager<IdentityRole<int>> roleManager)
    {
        var distinct = roleNames.Distinct(StringComparer.Ordinal).ToList();
        foreach (var role in distinct)
        {
            if (string.IsNullOrWhiteSpace(role))
                throw new InvalidOperationException("Role name may not be blank.");
            if (!await roleManager.RoleExistsAsync(role))
                throw new InvalidOperationException($"Role '{role}' does not exist.");
        }
    }
}

// ── Update ──────────────────────────────────────────────────────────────

public record UpdateRoleTemplateCommand(
    int Id,
    string Name,
    string? Description,
    string[] IncludedRoleNames) : IRequest<RoleTemplateResponseModel>;

public class UpdateRoleTemplateValidator : AbstractValidator<UpdateRoleTemplateCommand>
{
    public UpdateRoleTemplateValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.IncludedRoleNames)
            .NotNull()
            .Must(r => r.Length > 0)
            .WithMessage("Template must include at least one role.")
            .Must(r => r.Length <= 25)
            .WithMessage("Template may include at most 25 roles.");
    }
}

public class UpdateRoleTemplateHandler(
    AppDbContext db,
    RoleManager<IdentityRole<int>> roleManager,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<UpdateRoleTemplateCommand, RoleTemplateResponseModel>
{
    public async Task<RoleTemplateResponseModel> Handle(
        UpdateRoleTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.RoleTemplates
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"RoleTemplate {request.Id} not found.");

        if (entity.IsSystemDefault)
            throw new InvalidOperationException(
                "System-default templates cannot be edited. Duplicate the template first.");

        if (entity.Name != request.Name &&
            await db.RoleTemplates.AnyAsync(
                t => t.Id != request.Id && t.Name == request.Name, cancellationToken))
        {
            throw new InvalidOperationException(
                $"A role template named '{request.Name}' already exists.");
        }

        await CreateRoleTemplateHandler.EnsureRolesExistAsync(request.IncludedRoleNames, roleManager);

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.IncludedRoleNamesJson = JsonSerializer.Serialize(request.IncludedRoleNames);

        await db.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync("RoleTemplateUpdated", db.CurrentUserId ?? 0,
            entityType: "RoleTemplate", entityId: entity.Id,
            details: JsonSerializer.Serialize(new
            {
                name = entity.Name,
                includedRoles = request.IncludedRoleNames,
            }),
            ct: cancellationToken);

        var assigneeCount = await db.Set<SystemApiKey>()
            .CountAsync(k => k.RoleTemplateId == entity.Id, cancellationToken);

        return GetRoleTemplatesHandler.MapToResponse(entity, assigneeCount);
    }
}

// ── Delete (soft) ───────────────────────────────────────────────────────

public record DeleteRoleTemplateCommand(int Id) : IRequest<Unit>;

public class DeleteRoleTemplateHandler(
    AppDbContext db,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<DeleteRoleTemplateCommand, Unit>
{
    public async Task<Unit> Handle(
        DeleteRoleTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await db.RoleTemplates
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"RoleTemplate {request.Id} not found.");

        if (entity.IsSystemDefault)
            throw new InvalidOperationException(
                "System-default templates cannot be deleted.");

        if (entity.DeactivatedAt is not null)
            return Unit.Value;  // already deactivated

        entity.DeactivatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync("RoleTemplateDeleted", db.CurrentUserId ?? 0,
            entityType: "RoleTemplate", entityId: entity.Id,
            details: JsonSerializer.Serialize(new
            {
                name = entity.Name,
            }),
            ct: cancellationToken);

        return Unit.Value;
    }
}
