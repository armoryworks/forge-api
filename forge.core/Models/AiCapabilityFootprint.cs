using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>ai-fleet-orchestration D-crux: an enabled AI capability and its model size class.</summary>
public record AiCapabilityFootprint(string Name, AiModelClass ModelClass);
