
namespace Forge.Core.Models;

public record AdminUserResponseModel(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    string? Initials,
    string? AvatarColor,
    bool IsActive,
    string[] Roles,
    DateTimeOffset CreatedAt,
    bool HasPassword,
    bool HasPendingSetupToken,
    bool HasRfidIdentifier,
    bool HasBarcodeIdentifier,
    bool CanBeAssignedJobs,
    int ComplianceCompletedItems,
    int ComplianceTotalItems,
    string[] MissingComplianceItems,
    int? WorkLocationId,
    string? WorkLocationName,
    I9ComplianceStatus? I9Status,
    // Phase 3 / WU-06 / C1 — rollup template assignment.
    int? RoleTemplateId = null,
    string? RoleTemplateName = null,
    string[]? RoleTemplateIncludedRoles = null);
