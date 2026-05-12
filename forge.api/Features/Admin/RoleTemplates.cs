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
// Phase 3 / WU-06 / C1 — Role-template rollup CRUD + user assignment.
//
// A role template lets a tenant define a single named bundle of underlying
// roles (e.g., "FrontOffice" → OfficeManager + Controller + IT Admin) so a
// user wearing many hats in a small shop only has to be assigned the
// rollup. The auth path (RoleClaimsExpander) expands the template into JWT
// role claims at login time; downstream policy/[Authorize] checks see the
// underlying roles unchanged.
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

public record RoleTemplateAssigneeResponseModel(
    int UserId,
    string Email,
    string FirstName,
    string LastName);

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

        // Compute assignee counts in one query
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.Users
            .Where(u => u.RoleTemplateId.HasValue && ids.Contains(u.RoleTemplateId.Value))
            .GroupBy(u => u.RoleTemplateId!.Value)
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

        var assigneeCount = await db.Users
            .CountAsync(u => u.RoleTemplateId == entity.Id, cancellationToken);

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

        // Unassign all users currently on this template — keeps the FK clean
        // and ensures their next login gets fresh role claims.
        var assigned = await db.Users
            .Where(u => u.RoleTemplateId == entity.Id)
            .ToListAsync(cancellationToken);
        foreach (var u in assigned)
            u.RoleTemplateId = null;

        entity.DeactivatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync("RoleTemplateDeleted", db.CurrentUserId ?? 0,
            entityType: "RoleTemplate", entityId: entity.Id,
            details: JsonSerializer.Serialize(new
            {
                name = entity.Name,
                unassignedUserCount = assigned.Count,
            }),
            ct: cancellationToken);

        return Unit.Value;
    }
}

// ── Get assignees ───────────────────────────────────────────────────────

public record GetRoleTemplateAssigneesQuery(int TemplateId)
    : IRequest<List<RoleTemplateAssigneeResponseModel>>;

public class GetRoleTemplateAssigneesHandler(AppDbContext db)
    : IRequestHandler<GetRoleTemplateAssigneesQuery, List<RoleTemplateAssigneeResponseModel>>
{
    public async Task<List<RoleTemplateAssigneeResponseModel>> Handle(
        GetRoleTemplateAssigneesQuery request, CancellationToken cancellationToken)
    {
        return await db.Users
            .AsNoTracking()
            .Where(u => u.RoleTemplateId == request.TemplateId)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new RoleTemplateAssigneeResponseModel(
                u.Id, u.Email!, u.FirstName, u.LastName))
            .ToListAsync(cancellationToken);
    }
}

// ── Assign template to a user ───────────────────────────────────────────

public record AssignRoleTemplateCommand(int UserId, int TemplateId) : IRequest<Unit>;

public class AssignRoleTemplateValidator : AbstractValidator<AssignRoleTemplateCommand>
{
    public AssignRoleTemplateValidator()
    {
        RuleFor(x => x.UserId).GreaterThan(0);
        RuleFor(x => x.TemplateId).GreaterThan(0);
    }
}

public class AssignRoleTemplateHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<AssignRoleTemplateCommand, Unit>
{
    public async Task<Unit> Handle(
        AssignRoleTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.RoleTemplates
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId
                && t.DeactivatedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"RoleTemplate {request.TemplateId} not found or deactivated.");

        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException($"User {request.UserId} not found.");

        user.RoleTemplateId = template.Id;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to assign template: {errors}");
        }

        await auditWriter.WriteAsync("RoleTemplateAssigned", db.CurrentUserId ?? 0,
            entityType: "ApplicationUser", entityId: user.Id,
            details: JsonSerializer.Serialize(new
            {
                templateId = template.Id,
                templateName = template.Name,
            }),
            ct: cancellationToken);

        return Unit.Value;
    }
}

// ── Un-assign template from a user ──────────────────────────────────────

public record UnassignRoleTemplateCommand(int UserId) : IRequest<Unit>;

public class UnassignRoleTemplateHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISystemAuditWriter auditWriter)
    : IRequestHandler<UnassignRoleTemplateCommand, Unit>
{
    public async Task<Unit> Handle(
        UnassignRoleTemplateCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString())
            ?? throw new KeyNotFoundException($"User {request.UserId} not found.");

        var previousTemplateId = user.RoleTemplateId;
        if (previousTemplateId is null)
            return Unit.Value;

        user.RoleTemplateId = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to unassign template: {errors}");
        }

        await auditWriter.WriteAsync("RoleTemplateUnassigned", db.CurrentUserId ?? 0,
            entityType: "ApplicationUser", entityId: user.Id,
            details: JsonSerializer.Serialize(new
            {
                previousTemplateId = previousTemplateId,
            }),
            ct: cancellationToken);

        return Unit.Value;
    }
}
