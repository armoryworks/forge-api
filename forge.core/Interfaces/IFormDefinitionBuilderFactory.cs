using Forge.Core.Enums;

namespace Forge.Core.Interfaces;

/// <summary>
/// Routes form types to form-specific builders. Returns null for unknown types,
/// signaling fallback to the generic IFormDefinitionParser.
/// </summary>
public interface IFormDefinitionBuilderFactory
{
    IFormDefinitionBuilder? TryGetBuilder(ComplianceFormType formType);

    /// <summary>
    /// Try to get a hardcoded builder for a specific state withholding form.
    /// Returns null if no builder exists for that state (fallback to generic parser).
    /// </summary>
    IStateFormDefinitionBuilder? TryGetStateBuilder(string stateCode);
}
