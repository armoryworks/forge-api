namespace Forge.Core.Models;

public record GenerateWalkthroughResponseModel(
    int ModuleId,
    int StepCount,
    List<WalkthroughStep> Steps,
    string ContentJson);
