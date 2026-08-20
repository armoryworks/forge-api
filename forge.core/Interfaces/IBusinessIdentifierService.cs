using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Core.Interfaces;

/// <summary>
/// Registry for human-readable business numbers with rename history. Issue an active identifier on
/// entity creation; rename to supersede it (old number kept for resolution). Uniqueness is enforced
/// among *active* identifiers (a value resolves to at most one active owner).
/// </summary>
public interface IBusinessIdentifierService
{
    /// <summary>Record the entity's first active identifier (idempotent — returns the existing active row
    /// when the value already matches; treats a different active value as a rename).</summary>
    Task<BusinessIdentifier> IssueAsync(BusinessEntityType type, int entityId, string value, CancellationToken ct = default);

    /// <summary>Close the entity's current active identifier and open a new one. No-op when the value is
    /// unchanged. Throws when the new value is active on another entity.</summary>
    Task<BusinessIdentifier> RenameAsync(BusinessEntityType type, int entityId, string newValue, CancellationToken ct = default);

    /// <summary>The entity's current active value, or null.</summary>
    Task<string?> GetCurrentAsync(BusinessEntityType type, int entityId, CancellationToken ct = default);

    /// <summary>Resolve any value (active or retired) to its identifier row — active owner preferred.</summary>
    Task<BusinessIdentifier?> ResolveAsync(string value, CancellationToken ct = default);

    /// <summary>The entity's full identifier history, newest first.</summary>
    Task<IReadOnlyList<BusinessIdentifier>> GetHistoryAsync(BusinessEntityType type, int entityId, CancellationToken ct = default);

    /// <summary>True when <paramref name="value"/> is active on a different entity.</summary>
    Task<bool> IsActiveValueTakenAsync(string value, BusinessEntityType type, int entityId, CancellationToken ct = default);
}
