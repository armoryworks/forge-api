namespace Forge.Api.Authorization;

/// <summary>
/// Who may see and change the attachments, notes, activity and preview of one entity
/// type. <see cref="Roles"/> null means any signed-in user; <see cref="WriteRoles"/>
/// null means writes follow <see cref="Roles"/>.
/// </summary>
public sealed record EntityAccessRule(
    IReadOnlySet<string>? Roles,
    IReadOnlySet<string>? WriteRoles,
    EntityOwnership Ownership);
