using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class FormDefinitionBuilderFactory(
    IEnumerable<IFormDefinitionBuilder> builders,
    IEnumerable<IStateFormDefinitionBuilder> stateBuilders)
    : IFormDefinitionBuilderFactory
{
    private readonly Dictionary<ComplianceFormType, IFormDefinitionBuilder> _builders =
        builders.ToDictionary(b => b.FormType);

    private readonly Dictionary<string, IStateFormDefinitionBuilder> _stateBuilders =
        stateBuilders.ToDictionary(b => b.StateCode, StringComparer.OrdinalIgnoreCase);

    public IFormDefinitionBuilder? TryGetBuilder(ComplianceFormType formType) =>
        _builders.GetValueOrDefault(formType);

    public IStateFormDefinitionBuilder? TryGetStateBuilder(string stateCode) =>
        _stateBuilders.GetValueOrDefault(stateCode);
}
