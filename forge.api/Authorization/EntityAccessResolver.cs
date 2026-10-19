using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Authorization;

/// <inheritdoc />
public class EntityAccessResolver(AppDbContext db) : IEntityAccessResolver
{
    public async Task<bool> CanAccessAsync(
        string entityType, int entityId, ClaimsPrincipal? user, bool forWrite, CancellationToken ct)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        var rule = EntityAccessRules.Find(entityType);
        if (rule is null)
            return false;

        if (forWrite && rule.WriteRoles is not null)
            return InAnyRole(user, rule.WriteRoles);

        if (rule.Roles is null || InAnyRole(user, rule.Roles))
            return true;

        return TryGetUserId(user, out var userId) && await IsOwnerAsync(rule.Ownership, entityId, userId, ct);
    }

    public async Task<bool> CanAccessFileAsync(
        FileAttachment file, ClaimsPrincipal? user, bool forWrite, CancellationToken ct)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        if (!string.IsNullOrEmpty(file.RequiredRole) && !user.IsInRole(file.RequiredRole))
            return false;

        if (await CanAccessAsync(file.EntityType, file.EntityId, user, forWrite, ct))
            return true;

        var rule = EntityAccessRules.Find(file.EntityType);
        if (rule is null || rule.Ownership == EntityOwnership.None || !TryGetUserId(user, out var userId))
            return false;

        if (forWrite && rule.WriteRoles is not null)
            return false;

        if (file.UploadedById == userId)
            return true;

        return rule.Ownership == EntityOwnership.User && await IsLinkedToUserAsync(file.Id, userId, ct);
    }

    private Task<bool> IsOwnerAsync(EntityOwnership ownership, int entityId, int userId, CancellationToken ct) =>
        ownership switch
        {
            EntityOwnership.User => Task.FromResult(entityId == userId),
            EntityOwnership.Expense => db.Expenses.AnyAsync(e => e.Id == entityId && e.UserId == userId, ct),
            EntityOwnership.Employee => db.EmployeeProfiles.AnyAsync(p => p.Id == entityId && p.UserId == userId, ct),
            EntityOwnership.ComplianceSubmission =>
                db.ComplianceFormSubmissions.AnyAsync(s => s.Id == entityId && s.UserId == userId, ct),
            EntityOwnership.ChatRoom =>
                db.ChatRoomMembers.AnyAsync(m => m.ChatRoomId == entityId && m.UserId == userId, ct),
            _ => Task.FromResult(false),
        };

    private async Task<bool> IsLinkedToUserAsync(int fileId, int userId, CancellationToken ct) =>
        await db.PayStubs.AnyAsync(p => p.FileAttachmentId == fileId && p.UserId == userId, ct)
        || await db.TaxDocuments.AnyAsync(t => t.FileAttachmentId == fileId && t.UserId == userId, ct)
        || await db.IdentityDocuments.AnyAsync(d => d.FileAttachmentId == fileId && d.UserId == userId, ct);

    private static bool InAnyRole(ClaimsPrincipal user, IReadOnlySet<string> roles) => roles.Any(user.IsInRole);

    private static bool TryGetUserId(ClaimsPrincipal user, out int userId) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
