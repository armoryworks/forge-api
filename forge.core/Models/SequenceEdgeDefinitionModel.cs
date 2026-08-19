namespace Forge.Core.Models;

/// <summary>Dependency edge of a sequence definition (request and response shape).</summary>
public record SequenceEdgeDefinitionModel(string FromStepKey, string ToStepKey, bool IsRework = false);
