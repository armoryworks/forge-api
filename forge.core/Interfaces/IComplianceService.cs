using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// regulated-parts-safety C-1/C-3: resolves the shop's effective compliance requirements (the
/// additive union of active profiles) and enforces required fields at a process step.
/// </summary>
public interface IComplianceService
{
    Task<EffectiveComplianceRequirements> GetEffectiveRequirementsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the field keys required at <paramref name="processStep"/> that are missing from
    /// <paramref name="presentFields"/> — empty means the step satisfies compliance.
    /// </summary>
    Task<IReadOnlyList<string>> GetMissingRequiredFieldsAsync(
        string processStep, ISet<string> presentFields, CancellationToken ct = default);
}
